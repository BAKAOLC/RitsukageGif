using System.Collections.Concurrent;

namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession(
        WindowPreset preset,
        Func<Task<PresetContext>> observe,
        Func<PresetAction, Task> execute,
        PresetDebugger? debugger = null,
        Action<ScriptBindingRegistry>? configureBindings = null,
        Func<bool>? isCurrent = null,
        IMediaProcessingService? mediaService = null,
        ScriptParameters? parameters = null,
        IImageCompositionService? imageService = null,
        IInputChannelReader? inputReader = null,
        IStaticCaptureService? staticCaptureService = null,
        IVideoRecordingService? videoService = null,
        ITextRecognitionService? textRecognitionService = null,
        IBarcodeRecognitionService? barcodeRecognitionService = null,
        IPrivacyDetectionService? privacyService = null)
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Scripting.Session");
        private readonly ConcurrentQueue<string> _events = new();
        private readonly Dictionary<string, List<object?>> _handlers = new();
        private readonly Dictionary<string, PresetLayerState> _layers = new();

        private readonly Dictionary<string, object> _values =
            new(preset.InitialValues ?? new Dictionary<string, object>());

        private ScriptEngine? _engine;
        private PresetContext? _previous;

        private bool _pumping;
        private bool _recordingEnded;
        public PresetRuntime Runtime { get; } = new();
        public ScriptParameters Parameters { get; } = parameters ?? new ScriptParameters();
        internal IInputChannelReader? InputReader { get; } = inputReader;
        public string? LastError { get; private set; }
        private ScriptDiagnostic? LastDiagnostic { get; set; }
        private IReadOnlyList<ScriptStackFrame>? LastStackFrames { get; set; }

        public void Signal(string name)
        {
            _events.Enqueue(name);
            NotifyParameterWait();
        }

        public async Task RunAsync(CancellationToken token)
        {
            Logger.ZLogInformation($"Script session started. DebuggerAttached={debugger is not null}");
            try
            {
                var result = await RunCoreAsync(token);
                token.ThrowIfCancellationRequested();
                if (result is ScriptResult { IsOk: false } failure)
                {
                    LastError = UiText.Format("ScriptResultError", ScriptValueText.Convert(failure.Value));
                    LastDiagnostic = _engine?.FailureDiagnostic(LastError) ??
                                     new ScriptDiagnostic(preset.Program?.Line ?? 1, preset.Program?.Column ?? 1,
                                         LastError, preset.SourceFile);
                    LastStackFrames = _engine?.FailureFrames;
                    Logger.ZLogWarning(
                        $"Script session returned an error result. Line={LastDiagnostic.Line} Column={LastDiagnostic.Column}");
                    await ExecuteActionAsync(new("cancel"));
                }
                else if (!_recordingEnded)
                {
                    await ExecuteActionAsync(new("stop"));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogInformation($"Script session canceled.");
                await ExecuteActionAsync(new("cancel"));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LastDiagnostic = ex switch
                {
                    ScriptExecutionException execution => execution.Diagnostic,
                    PresetSyntaxException syntax => syntax.Diagnostic,
                    _ => new(preset.Program?.Line ?? 1, preset.Program?.Column ?? 1, ex.Message,
                        preset.SourceFile),
                };
                LastStackFrames = (ex as ScriptExecutionException)?.Frames;
                Logger.ZLogError(
                    $"Script session failed. ExceptionType={ex.GetType().Name} Line={LastDiagnostic.Line} Column={LastDiagnostic.Column} StackTrace={ex.StackTrace}");
                await ExecuteActionAsync(new("cancel"));
            }
            finally
            {
                Logger.ZLogInformation(
                    $"Script session ended. Failed={LastError is not null} Canceled={token.IsCancellationRequested}");
                if (isCurrent?.Invoke() != false) debugger?.Complete(LastError, LastDiagnostic, LastStackFrames);
            }
        }

        private async Task ExecuteActionAsync(PresetAction action)
        {
            if (action.Kind == "start") InitializePlayback(PresetMotion.Now);
            await execute(action);
            _recordingEnded = action.Kind switch
            {
                "stop" or "cancel" or "finish" => true,
                "start" or "startMany" => false,
                _ => _recordingEnded,
            };
        }

        private async Task<object?> RunCoreAsync(CancellationToken token)
        {
            Runtime.Publish(_values, _layers);
            var bindings = CaptureScriptLibrary.CreateBindings(this);
            configureBindings?.Invoke(bindings);
            var engine = new ScriptEngine(debugger, bindings);
            _engine = engine;
            foreach (var pair in preset.Constants) engine.Define(pair.Key, pair.Value);
            foreach (var pair in _values) engine.Define(pair.Key, pair.Value, false);
            return await engine.RunAsync(preset.Program!,
                preset.SourceFile ?? Path.Combine(Environment.CurrentDirectory, "capture.rgpreset"), token);
        }

        private async Task WaitAsync(double seconds, CancellationToken token)
        {
            var until = PresetMotion.Now + seconds;
            do
            {
                await PumpAsync(token);
                var remaining = until - PresetMotion.Now;
                if (remaining <= 0) break;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(.02, remaining)), token);
            } while (true);
        }

        private async Task PumpAsync(CancellationToken token)
        {
            if (_pumping) return;
            _pumping = true;
            try
            {
                var context = await observe();
                var old = _previous;
                _previous = context;
                if (context.Window?.Id != old?.Window?.Id)
                {
                    if (old?.Window is not null) _events.Enqueue("lost");
                    if (context.Window is not null) _events.Enqueue("found");
                }
                else if (context.Window is { } current && old?.Window is { } before)
                {
                    if (current.Title != before.Title) _events.Enqueue("title");
                    if (current.Bounds.X != before.Bounds.X || current.Bounds.Y != before.Bounds.Y)
                        _events.Enqueue("moved");
                    if (current.Bounds.Width != before.Bounds.Width || current.Bounds.Height != before.Bounds.Height)
                        _events.Enqueue("resized");
                }

                var budget = 128;
                while (_events.TryDequeue(out var name))
                {
                    token.ThrowIfCancellationRequested();
                    if (--budget == 0) throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
                    if (_handlers.TryGetValue(name, out var handlers))
                        foreach (var handler in handlers.ToArray())
                        {
                            var result = await _engine!.InvokeAsync(handler, [], token);
                            if (result is ScriptResult { IsOk: false } failure)
                            {
                                var message = UiText.Format("ScriptResultError",
                                    ScriptValueText.Convert(failure.Value));
                                var diagnostic = _engine.FailureDiagnostic(message);
                                throw new ScriptExecutionException(diagnostic?.Line ?? 1, diagnostic?.Column ?? 1,
                                    message, sourceFile: diagnostic?.Source, frames: _engine.FailureFrames);
                            }
                        }
                }
            }
            finally
            {
                _pumping = false;
            }
        }

        private static Dictionary<string, object?> Context(PresetContext c)
        {
            return new()
            {
                ["exists"] = c.Window is not null, ["recording"] = c.Recording, ["busy"] = c.Busy, ["idle"] = !c.Busy,
                ["title"] = c.Window?.Title ?? "", ["width"] = (double)(c.Window?.Bounds.Width ?? 0),
                ["height"] = (double)(c.Window?.Bounds.Height ?? 0), ["elapsed"] = c.RecordingElapsed,
                ["outputs"] = (c.Outputs ?? []).Cast<object?>().ToList(),
                ["streams"] = StreamValues(c),
            };
        }

        private static string RequireName(object? value)
        {
            return value as string ?? throw new ArgumentException(UiText.Get("ScriptTypeError"));
        }

        private static double Seconds(object? value)
        {
            var seconds = ScriptEngine.Number(value);
            if (seconds is <= 0 or > 86400) throw new ArgumentException(UiText.Get("DslDuration"));
            return seconds;
        }

        private PresetLayer Layer(string name)
        {
            return preset.Layout?.Layers.SingleOrDefault(l => l.Name == name) ??
                   throw new ArgumentException(UiText.Format("DslUnknownName", name));
        }

        private void UpdateLayer(string operation, IReadOnlyList<object?> args)
        {
            var minimum = operation switch { "animate" => 6, "move" or "crop" => 5, _ => 2 };
            if (args.Count < minimum || args.Count > minimum + (operation == "animate" ? 1 : 0))
                throw new ArgumentException(UiText.Format("DslArguments", operation));
            var name = RequireName(args[0]);
            var definition = Layer(name);
            var state = _layers.GetValueOrDefault(name) ?? new PresetLayerState();
            switch (operation)
            {
                case "move":
                case "animate":
                case "crop":
                {
                    if (args.Count < 5) throw new ArgumentException(UiText.Format("DslArguments", operation));
                    var numbers = args.Skip(1).Take(4).Select(a => checked((int)Math.Round(ScriptEngine.Number(a))))
                        .ToArray();
                    var rect = new PixelRect(numbers[0], numbers[1], numbers[2], numbers[3]);
                    if (rect.IsEmpty || rect.Width > 16384 || rect.Height > 16384 || Math.Abs((long)rect.X) > 131072 ||
                        Math.Abs((long)rect.Y) > 131072) throw new ArgumentException(UiText.Get("InvalidRecording"));
                    switch (operation)
                    {
                        case "crop":
                            state = state with { Crop = rect };
                            break;
                        case "move":
                            state = state with { Placement = rect, Motion = null };
                            break;
                        default:
                            var seconds = Seconds(args[5]);
                            var easing = args.Count > 6 ? RequireName(args[6]) : "linear";
                            var now = Runtime.Now;
                            var from = state.Motion?.At(now) ?? state.Placement ?? definition.Placement;
                            state = state with
                            {
                                Placement = rect, Motion = new(from, rect, now, seconds, easing),
                            };
                            break;
                    }

                    break;
                }
                case "visible":
                    state = state with { Visible = ScriptEngine.Truth(args[1]) };
                    break;
                default:
                    var opacity = ScriptEngine.Number(args[1]);
                    if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
                        throw new ArgumentException(UiText.Format("DslRange", 0, 1));
                    state = state with { Opacity = opacity };
                    break;
            }

            _layers[name] = state;
            Runtime.Publish(_values, _layers);
        }
    }
}
