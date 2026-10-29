using System.Collections.Concurrent;
using System.Globalization;

namespace RitsukageGif.Scripting
{
    public sealed class PresetDebugger
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Scripting.Debugger");

        private readonly ConcurrentDictionary<string, HashSet<int>> _fileBreakpoints =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Queue<string> _output = new();
        private readonly Lock _sync = new();
        private volatile HashSet<int> _breakpoints = [];
        private volatile bool _pauseRequested;
        private volatile TaskCompletionSource? _resume;

        private volatile PresetDebugSnapshot _snapshot =
            new(false, false, null, 0, 0, new Dictionary<string, string>(), [], null);

        public string? DefaultSource { get; set; }
        public bool Paused => _resume is not null;

        public PresetDebugSnapshot Snapshot => _snapshot;
        public event Action? Changed;

        public void SetBreakpoints(IEnumerable<int> lines, string? file = null)
        {
            var points = lines.Where(n => n > 0).ToHashSet();
            file ??= DefaultSource;
            if (file is not null) _fileBreakpoints[Path.GetFullPath(file)] = points;
            else _breakpoints = points;
            Logger.ZLogDebug(
                $"Debugger breakpoints updated. BreakpointCount={points.Count} FileSpecific={file is not null}");
        }

        public void Pause()
        {
            _pauseRequested = true;
            Logger.ZLogDebug($"Debugger pause requested.");
        }

        public void Continue(bool step = false)
        {
            TaskCompletionSource? pending;
            lock (_sync)
            {
                _pauseRequested = step;
                pending = _resume;
                _resume = null;
                _snapshot = Snapshot with { Paused = false };
            }

            Logger.ZLogDebug($"Debugger continued. SingleStep={step} WasPaused={pending is not null}");
            pending?.TrySetResult();
            Changed?.Invoke();
        }

        public async Task BeforeAsync(PresetAction action, string? state, PresetContext context,
            CancellationToken token)
        {
            var values = new Dictionary<string, string>();
            foreach (var pair in context.Constants)
                values[pair.Key] = pair.Value.ToString(CultureInfo.InvariantCulture);
            foreach (var pair in context.Variables ?? new Dictionary<string, object>())
                values[pair.Key] = Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? "";
            foreach (var name in new[]
                     {
                         "exists", "recording", "busy", "idle", "title", "width", "height", "elapsed",
                         "recording.elapsed",
                     })
                values[name] =
                    Convert.ToString(new PresetVariable(name).Evaluate(context), CultureInfo.InvariantCulture) ??
                    "";
            TaskCompletionSource? gate;
            lock (_sync)
            {
                if (Snapshot.Finished) return;
                var points = DefaultSource is null
                    ? _breakpoints
                    : _fileBreakpoints.GetValueOrDefault(Path.GetFullPath(DefaultSource)) ?? _breakpoints;
                var pause = _pauseRequested || action.Kind == "breakpoint" || points.Contains(action.Line);
                gate = pause ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
                _resume = gate;
                _snapshot = new(pause, false, state, action.Line, action.Column, values, [.. _output], null,
                    DefaultSource);
            }

            Changed?.Invoke();
            if (gate is not null)
            {
                Logger.ZLogDebug($"Preset debugger paused. Line={action.Line} Column={action.Column}");
                await gate.Task.WaitAsync(token);
            }
        }

        public async Task BeforeScriptAsync(ScriptNode node, string file, IReadOnlyList<string> calls,
            Func<Dictionary<string, object?>> inspect, CancellationToken token)
        {
            if (!_pauseRequested &&
                !(_fileBreakpoints.GetValueOrDefault(Path.GetFullPath(file)) ?? _breakpoints).Contains(node.Line))
                return;
            var values = inspect().Where(p => p.Value is not ScriptNative).Take(200)
                .ToDictionary(p => p.Key, p => ScriptEngine.Format(p.Value));
            TaskCompletionSource gate;
            lock (_sync)
            {
                if (Snapshot.Finished) return;
                if (!_pauseRequested &&
                    !(_fileBreakpoints.GetValueOrDefault(Path.GetFullPath(file)) ?? _breakpoints).Contains(node.Line))
                    return;
                gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _resume = gate;
                _snapshot = new(true, false, "script", node.Line, node.Column, values, [.. _output], null, file,
                    [.. calls]);
            }

            Changed?.Invoke();
            Logger.ZLogDebug($"Script debugger paused. Line={node.Line} Column={node.Column} CallDepth={calls.Count}");
            await gate.Task.WaitAsync(token);
        }

        public void Log(string value)
        {
            lock (_sync)
            {
                _output.Enqueue(value);
                while (_output.Count > 300) _output.Dequeue();
                _snapshot = Snapshot with { Output = [.. _output] };
            }

            Changed?.Invoke();
        }

        public void Complete(string? error = null, ScriptDiagnostic? diagnostic = null,
            IReadOnlyList<ScriptStackFrame>? frames = null)
        {
            Logger.ZLogDebug(
                $"Debugger completed. Failed={error is not null || diagnostic is not null} Line={diagnostic?.Line ?? 0} Column={diagnostic?.Column ?? 0}");
            lock (_sync)
            {
                _resume?.TrySetCanceled();
                _resume = null;
                _snapshot = Snapshot with
                {
                    Paused = false, Finished = true, Error = error ?? Snapshot.Error,
                    ErrorDiagnostic = diagnostic ?? Snapshot.ErrorDiagnostic,
                    StackFrames = frames ?? Snapshot.StackFrames, Source = diagnostic?.Source ?? Snapshot.Source,
                    Line = diagnostic?.Line ?? Snapshot.Line, Column = diagnostic?.Column ?? Snapshot.Column,
                };
            }

            Changed?.Invoke();
        }
    }
}
