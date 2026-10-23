namespace RitsukageGif.Scripting
{
    public sealed class PresetMachine(WindowPreset preset, PresetDebugger? debugger = null)
    {
        private readonly Dictionary<string, object> _variables =
            new(preset.InitialValues ?? new Dictionary<string, object>());

        private readonly Dictionary<string, PresetLayerState> _layers = new();
        private readonly Stack<Dictionary<string, object?>> _calls = new();
        private readonly List<string> _emitted = [];
        public PresetRuntime Runtime { get; } = new();
        public IReadOnlyDictionary<string, object> Values => _variables;
        private readonly LinkedList<PresetAction> _pending = new();
        private readonly HashSet<PresetRule> _fired = [];
        private readonly HashSet<PresetRule> _conditions = [];
        private PresetState? _state;
        private PresetContext? _previous;
        private double _entered, _started, _resumeAt;
        private bool _suspended;
        private bool _handlingError;
        private int _activeLine = 1, _activeColumn = 1;
        public bool Finished { get; private set; }
        public string? State => _state?.Name;
        public string? LastError { get; private set; }

        public async Task StepAsync(double now, Func<PresetContext> observe, IReadOnlyCollection<string> signals,
            Func<PresetAction, Task> execute, CancellationToken cancellation)
        {
            if (Finished) return;
            var initializing = _state is null;
            if (initializing)
            {
                _started = now;
                Enter(preset.Initial, now);
            }

            PresetContext Context() => observe() with
            {
                Elapsed = now - _entered, Constants = preset.Constants, Variables = _variables
            };

            var context = Context();
            var events = new HashSet<string>(signals.Concat(_emitted), StringComparer.Ordinal);
            _emitted.Clear();
            var old = _previous;
            if (!initializing) _previous = context;
            if (context.Window is { } current)
            {
                if (current.Id != old?.Window?.Id) events.Add("found");
                else if (old.Window is { } before)
                {
                    if (current.Bounds.X != before.Bounds.X || current.Bounds.Y != before.Bounds.Y) events.Add("moved");
                    if (current.Bounds.Width != before.Bounds.Width || current.Bounds.Height != before.Bounds.Height)
                        events.Add("resized");
                    if (current.Title != before.Title) events.Add("title");
                }
            }

            if (old?.Window is { } previousWindow && context.Window?.Id != previousWindow.Id) events.Add("lost");
            if (context.Recording && old?.Recording != true) events.Add("recording");
            if (!context.Recording && old?.Recording == true) events.Add("stopped");
            try
            {
                // Local rules take priority; one rule per observation prevents competing transitions.
                var rule = initializing || _handlingError
                    ? null
                    : _state!.Rules.Concat(preset.Rules).FirstOrDefault(r => Matches(r, context, events, now));
                if (rule is not null)
                {
                    while (_calls.Count > 0) RestoreCall();
                    _pending.Clear();
                    _suspended = false;
                    Prepend(rule.Actions);
                }

                var budget = 256;
                while (_pending.First is { } node && !Finished)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (--budget == 0) throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
                    var action = node.Value;
                    if (action.Line > 0)
                    {
                        _activeLine = action.Line;
                        _activeColumn = action.Column;
                    }

                    if (debugger is not null && action.Line > 0 && !(action.Kind is "wait" or "await" && _suspended))
                        await debugger.BeforeAsync(action, State, Context(), cancellation);
                    if (action.Kind is "wait" or "await")
                    {
                        if (!_suspended)
                        {
                            var seconds = action.Arguments is { Count: > 0 }
                                ? action.Arguments[0].Number(Context())
                                : action.Seconds;
                            if (seconds <= 0 || seconds > 86400)
                                throw new InvalidOperationException(UiText.Get("DslDuration"));
                            _resumeAt = now + seconds;
                            _suspended = true;
                        }

                        if (action.Kind == "await" && action.Condition!.Boolean(Context()))
                        {
                        }
                        else if (now < _resumeAt) break;
                        else if (action.Kind == "await") throw new TimeoutException(UiText.Get("DslAwaitTimeout"));

                        _suspended = false;
                        _pending.RemoveFirst();
                        continue;
                    }

                    _pending.RemoveFirst();
                    switch (action.Kind)
                    {
                        case "assign":
                            _variables[action.Target!] = Evaluate(action.Arguments![0], Context());
                            Runtime.Publish(_variables, _layers);
                            break;
                        case "log":
                            debugger?.Log(Convert.ToString(action.Arguments![0].Evaluate(Context()),
                                System.Globalization.CultureInfo.InvariantCulture) ?? ""); break;
                        case "assert":
                            if (!action.Arguments![0].Boolean(Context()))
                                throw new InvalidOperationException(UiText.Get("DslAssertion"));
                            break;
                        case "emit": _emitted.Add(action.Target!); break;
                        case "breakpoint": break;
                        case "move":
                        case "visible":
                        case "opacity":
                            UpdateLayer(action, Context());
                            break;
                        case "call":
                            if (_calls.Count >= 32)
                                throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
                            var function = preset.Functions!.Single(f => f.Name == action.Target);
                            var arguments = action.Arguments!.Select(a => Evaluate(a, Context())).ToArray();
                            var saved = new Dictionary<string, object?>();
                            for (var i = 0; i < function.Parameters.Count; i++)
                            {
                                var name = function.Parameters[i];
                                saved[name] = _variables.GetValueOrDefault(name);
                                _variables[name] = arguments[i];
                            }

                            _calls.Push(saved);
                            _pending.AddFirst(new PresetAction("endcall"));
                            Prepend(function.Body);
                            break;
                        case "return":
                            while (_pending.First is { } pending && pending.Value.Kind != "endcall")
                                _pending.RemoveFirst();
                            break;
                        case "endcall": RestoreCall(); break;
                        case "while":
                            if (action.Condition!.Boolean(Context()))
                            {
                                _pending.AddFirst(action);
                                _pending.AddFirst(new PresetAction("endloop"));
                                Prepend(action.Body!);
                            }

                            break;
                        case "break":
                            while (_pending.First is { } pending && pending.Value.Kind != "endloop")
                                _pending.RemoveFirst();
                            if (_pending.First is not null) _pending.RemoveFirst();
                            if (_pending.First?.Value.Kind is "while" or "repeat") _pending.RemoveFirst();
                            break;
                        case "endloop": break;
                        case "goto":
                            await ExitAsync(Context(), execute, cancellation);
                            cancellation.ThrowIfCancellationRequested();
                            Enter(action.Target!, now);
                            break;
                        case "finish":
                            await ExitAsync(Context(), execute, cancellation);
                            await execute(action);
                            Finished = true;
                            _pending.Clear();
                            debugger?.Complete();
                            break;
                        case "if":
                            Prepend(action.Condition!.Boolean(Context()) ? action.Body! : action.Otherwise!); break;
                        case "repeat":
                            if (action.Seconds > 1) _pending.AddFirst(action with { Seconds = action.Seconds - 1 });
                            _pending.AddFirst(new PresetAction("endloop"));
                            Prepend(action.Body!);
                            break;
                        default: await execute(action); break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = UiText.Format("DslDiagnostic", _activeLine, _activeColumn, ex.Message);
                _pending.Clear();
                _suspended = false;
                var handler = _state!.Rules.Concat(preset.Rules)
                    .FirstOrDefault(r => r.Event == "error" && (r.Condition?.Boolean(Context()) ?? true));
                if (_handlingError || events.Contains("error") || handler is null)
                {
                    Finished = true;
                    debugger?.Complete(LastError);
                    throw;
                }

                _handlingError = true;
                Prepend(handler.Actions);
                // The handler runs on the next observation, without recursively dispatching failures.
                _previous = Context();
            }
        }

        private static object Evaluate(PresetExpression expression, PresetContext context)
        {
            var value = expression.Evaluate(context);
            if (value is double number && !double.IsFinite(number))
                throw new InvalidOperationException(UiText.Get("DslNumberRequired"));
            return value;
        }

        private void RestoreCall()
        {
            if (!_calls.TryPop(out var saved)) return;
            foreach (var pair in saved)
                if (pair.Value is null) _variables.Remove(pair.Key);
                else _variables[pair.Key] = pair.Value;
        }

        private void UpdateLayer(PresetAction action, PresetContext context)
        {
            var layer = _layers.GetValueOrDefault(action.Target!) ?? new PresetLayerState();
            var args = action.Arguments!;
            if (action.Kind == "move")
            {
                var values = args.Skip(1).Select(a => checked((int)Math.Round(a.Number(context)))).ToArray();
                var rect = new PixelRect(values[0], values[1], values[2], values[3]);
                if (rect.IsEmpty || Math.Abs((long)rect.X) > 16384 || Math.Abs((long)rect.Y) > 16384 ||
                    rect.Width > 16384 || rect.Height > 16384)
                    throw new InvalidOperationException(UiText.Get("InvalidRecording"));
                layer = layer with { Placement = rect };
            }
            else if (action.Kind == "visible") layer = layer with { Visible = args[1].Boolean(context) };
            else
            {
                var opacity = args[1].Number(context);
                if (opacity is < 0 or > 1) throw new InvalidOperationException(UiText.Format("DslRange", 0, 1));
                layer = layer with { Opacity = opacity };
            }

            _layers[action.Target!] = layer;
            Runtime.Publish(_variables, _layers);
        }

        private bool Matches(PresetRule rule, PresetContext context, HashSet<string> events, double now)
        {
            var allowed = rule.Condition?.Boolean(context) ?? true;
            if (rule.Event == "when")
            {
                if (!allowed)
                {
                    _conditions.Remove(rule);
                    return false;
                }

                return _conditions.Add(rule);
            }

            if (!allowed) return false;
            if (rule.Event == "after")
            {
                var start = preset.Rules.Contains(rule) ? _started : _entered;
                return now - start >= rule.Delay && _fired.Add(rule);
            }

            return events.Contains(rule.Event);
        }

        private void Enter(string name, double now)
        {
            if (_state is not null)
            {
                foreach (var rule in _state.Rules)
                {
                    _fired.Remove(rule);
                    _conditions.Remove(rule);
                }
            }

            while (_calls.Count > 0) RestoreCall();
            Runtime.Publish(_variables, _layers);
            _state = preset.States.Single(s => s.Name == name);
            _entered = now;
            _handlingError = false;
            _pending.Clear();
            _suspended = false;
            Prepend(_state.Enter);
        }

        private void Prepend(IReadOnlyList<PresetAction> actions)
        {
            for (var i = actions.Count - 1; i >= 0; i--) _pending.AddFirst(actions[i]);
        }

        private async Task ExitAsync(PresetContext context, Func<PresetAction, Task> execute, CancellationToken token)
        {
            var work = new Stack<PresetAction>(_state!.Exit.Reverse());
            var budget = 256;
            while (work.TryPop(out var action))
            {
                token.ThrowIfCancellationRequested();
                if (--budget == 0) throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
                if (action.Kind == "if")
                {
                    foreach (var child in (action.Condition!.Boolean(context) ? action.Body! : action.Otherwise!)
                             .Reverse())
                        work.Push(child);
                }
                else if (action.Kind == "repeat")
                {
                    if (action.Seconds > 1) work.Push(action with { Seconds = action.Seconds - 1 });
                    foreach (var child in action.Body!.Reverse()) work.Push(child);
                }
                else if (action.Kind == "assign")
                {
                    _variables[action.Target!] = Evaluate(action.Arguments![0], context);
                    Runtime.Publish(_variables, _layers);
                }
                else if (action.Kind is "move" or "visible" or "opacity") UpdateLayer(action, context);
                else if (action.Kind == "log")
                    debugger?.Log(Convert.ToString(action.Arguments![0].Evaluate(context)) ?? "");
                else if (action.Kind == "assert")
                {
                    if (!action.Arguments![0].Boolean(context))
                        throw new InvalidOperationException(UiText.Get("DslAssertion"));
                }
                else if (action.Kind == "emit") _emitted.Add(action.Target!);
                else if (action.Kind != "breakpoint") await execute(action);
            }
        }
    }
}
