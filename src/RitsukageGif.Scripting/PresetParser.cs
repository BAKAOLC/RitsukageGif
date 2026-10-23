using System.Globalization;

namespace RitsukageGif.Scripting
{
    internal sealed class PresetParser(string source, ScriptBindingRegistry? bindings = null)
    {
        private readonly RecordingSettingsBinding _recordingSettings =
            new(bindings ?? CaptureScriptLibrary.CreateBindings());

        private readonly IReadOnlyList<PresetToken> _tokens = PresetLexer.Scan(source);
        private readonly Dictionary<string, double> _constants = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        private int _at, _depth, _functionDepth, _loopDepth;
        private PresetToken Current => _tokens[_at];

        private static readonly string[] Variables =
            ["exists", "recording", "busy", "idle", "title", "width", "height", "elapsed", "recording.elapsed"];

        private PresetContext Sample => new(new PresetWindowState(1, "", new PixelRect(0, 0, 1920, 1080)), false, false,
            0,
            0, _constants, 100, _values);

        private PresetSyntaxException Error(string key, params object[] values) => new(Current.Line, Current.Column,
            values.Length == 0 ? UiText.Get(key) : UiText.Format(key, values));

        private bool Is(string text) => !Current.Quoted && Current.Text == text;

        private bool Accept(string text)
        {
            if (!Is(text)) return false;
            _at++;
            return true;
        }

        private void Expect(string text)
        {
            if (!Accept(text)) throw Error("DslExpected", text, Current.Text);
        }

        private string String()
        {
            if (!Current.Quoted) throw Error("DslExpected", "\"…\"", Current.Text);
            return _tokens[_at++].Text;
        }

        private string Name()
        {
            if (Current.Quoted || Current.Text.Length == 0 ||
                !(char.IsLetter(Current.Text[0]) || Current.Text[0] == '_'))
                throw Error("DslExpected", "identifier", Current.Text);
            return _tokens[_at++].Text;
        }

        private double Numeric()
        {
            if (Current.Quoted ||
                !double.TryParse(Current.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out var n) ||
                !double.IsFinite(n))
                throw Error("DslNumberRequired");
            _at++;
            return n;
        }

        private void EnterDepth()
        {
            if (++_depth > 32) throw Error("DslTooComplex");
        }

        public IReadOnlyList<WindowPreset> Parse()
        {
            try
            {
                return ParseDocument();
            }
            catch (Exception ex) when (ex is not PresetSyntaxException &&
                                       ex is FormatException or InvalidOperationException or OverflowException
                                           or ArgumentException)
            {
                throw new PresetSyntaxException(Current.Line, Current.Column, ex.Message);
            }
        }

        private IReadOnlyList<WindowPreset> ParseDocument()
        {
            var result = new List<WindowPreset>();
            while (!Is("<eof>"))
            {
                _constants.Clear();
                _types.Clear();
                _values.Clear();
                Expect("preset");
                var name = String();
                if (string.IsNullOrWhiteSpace(name) || result.Any(p => p.Name == name))
                    throw Error("DslDuplicate", name);
                var preset = WindowPreset.ForWindow("*", "*") with { Name = name, Follow = true };
                var functions = new List<PresetFunction>();
                var states = new List<PresetState>();
                var rules = new List<PresetRule>();
                var outputs = new List<PresetOutput>();
                var declarations = new HashSet<string>();
                Expect("{");
                while (!Accept("}"))
                {
                    var kind = Current.Text;
                    if (kind is not ("const" or "let" or "fn" or "state" or "on" or "when" or "after" or "save") &&
                        !declarations.Add(kind))
                        throw Error("DslDuplicate", kind);
                    switch (kind)
                    {
                        case "const":
                        case "let":
                            _at++;
                            var id = Name();
                            if (Variables.Contains(id) || _constants.ContainsKey(id) || _types.ContainsKey(id))
                                throw Error("DslDuplicate", id);
                            Expect("=");
                            var value = Expression();
                            if (kind == "const")
                            {
                                RequireNumeric(value, false);
                                _constants.Add(id, value.Number(Sample));
                            }
                            else
                            {
                                RequireInitial(value);
                                _values.Add(id, value.Evaluate(Sample));
                                _types.Add(id, TypeOf(value));
                            }

                            Expect(";");
                            break;
                        case "fn": functions.Add(Function()); break;
                        case "script":
                            _at++;
                            var parser = new ScriptParser(_tokens, _at);
                            var program = parser.ParseBlock();
                            _at = parser.Position;
                            preset = preset with { Program = program };
                            break;
                        case "window":
                            var match = Window();
                            preset = preset with { Process = match.Process, Title = match.Title };
                            break;
                        case "source": preset = preset with { Source = Source() }; break;
                        case "missing": preset = preset with { Missing = Fallback() }; break;
                        case "area":
                            var area = Area();
                            preset = preset with
                            {
                                ClientArea = area.Client, Area = area.Values, Absolute = area.Absolute
                            };
                            break;
                        case "follow":
                            _at++;
                            preset = preset with { Follow = Boolean() };
                            Expect(";");
                            break;
                        case "record":
                            _at++;
                            preset = preset with { Settings = Settings() };
                            break;
                        case "save":
                            _at++;
                            Expect("to");
                            var directory = String();
                            Expect("as");
                            var filename = String();
                            Expect(";");
                            PresetOutputNaming.Validate(directory, filename);
                            outputs.Add(new PresetOutput(directory, filename));
                            break;
                        case "layout": preset = preset with { Layout = Layout() }; break;
                        case "initial":
                            _at++;
                            preset = preset with { Initial = Name() };
                            Expect(";");
                            break;
                        case "state": states.Add(State()); break;
                        case "on":
                        case "when":
                        case "after": rules.Add(Rule()); break;
                        default: throw Error("DslUnexpected", Current.Text);
                    }
                }

                if (!preset.Absolute && preset.Layout?.Layers.All(l => l.Absolute || l.Media is not null) != true &&
                    preset.Process == "*" &&
                    preset.Title == "*") throw Error("DslWindowRequired");
                if (states.Count == 0 && preset.Program is null) throw Error("DslStateRequired");
                if (preset.Program is not null && states.Count > 0) throw Error("DslScriptState");
                if (states.Select(s => s.Name).Distinct().Count() != states.Count) throw Error("DslDuplicate", "state");
                preset = preset with
                {
                    Constants = new Dictionary<string, double>(_constants), States = states, Rules = rules,
                    Initial = preset.Initial.Length == 0 ? states.FirstOrDefault()?.Name ?? "script" : preset.Initial,
                    Outputs = outputs,
                    Functions = functions, InitialValues = new Dictionary<string, object>(_values)
                };
                if (preset.Program is null && !states.Any(s => s.Name == preset.Initial))
                    throw Error("DslUnknownName", preset.Initial);
                foreach (var action in Walk(states
                             .SelectMany(s => s.Enter.Concat(s.Exit).Concat(s.Rules.SelectMany(r => r.Actions)))
                             .Concat(rules.SelectMany(r => r.Actions))))
                    if (action.Kind == "goto" && !states.Any(s => s.Name == action.Target))
                        throw Error("DslUnknownName", action.Target!);
                if (functions.Select(f => f.Name).Distinct().Count() != functions.Count)
                    throw Error("DslDuplicate", "fn");
                foreach (var action in Walk(states
                             .SelectMany(st => st.Enter.Concat(st.Exit).Concat(st.Rules.SelectMany(r => r.Actions)))
                             .Concat(rules.SelectMany(r => r.Actions)).Concat(functions.SelectMany(f => f.Body))))
                {
                    if (action.Kind == "call")
                    {
                        var function = functions.SingleOrDefault(f => f.Name == action.Target);
                        if (function is null) throw Error("DslUnknownName", action.Target!);
                        if (function.Parameters.Count != action.Arguments!.Count)
                            throw Error("DslArguments", action.Target!);
                    }

                    if (action.Kind is "move" or "visible" or "opacity" &&
                        preset.Layout?.Layers.Any(l => l.Name == action.Target) != true)
                        throw Error("DslUnknownName", action.Target!);
                }

                if (preset.Absolute && preset.Source != CaptureSourceKind.Screen) throw Error("DslAbsoluteScreen");
                ValidateFallbacks(preset);
                result.Add(preset);
            }

            return result;
        }

        private static IEnumerable<PresetAction> Walk(IEnumerable<PresetAction> actions)
        {
            foreach (var action in actions)
            {
                yield return action;
                foreach (var child in Walk((action.Body ?? []).Concat(action.Otherwise ?? []))) yield return child;
            }
        }

        private void ValidateFallbacks(WindowPreset preset)
        {
            var layers = preset.Layout?.Layers ?? [];
            if (preset.Missing is { Kind: "source" }) throw Error("DslFallbackReference");
            foreach (var layer in layers)
            {
                var seen = new HashSet<string> { layer.Name };
                var current = layer;
                while (current.Missing.Kind == "source")
                {
                    if (!seen.Add(current.Missing.Value)) throw Error("DslFallbackCycle");
                    current = layers.SingleOrDefault(l => l.Name == current.Missing.Value) ??
                              throw Error("DslUnknownName", current.Missing.Value);
                }
            }
        }

        private (string Process, string Title) Window()
        {
            Expect("window");
            Expect("process");
            var process = String();
            Expect("title");
            var title = String();
            Expect(";");
            if (string.IsNullOrWhiteSpace(process) || string.IsNullOrWhiteSpace(title) ||
                process == "*" && title == "*")
                throw Error("DslWindowRequired");
            return (process, title);
        }

        private CaptureSourceKind Source()
        {
            Expect("source");
            var sourceKind = Accept("screen") ? CaptureSourceKind.Screen :
                Accept("window") ? CaptureSourceKind.Window : throw Error("DslExpected", "screen/window", Current.Text);
            Expect(";");
            return sourceKind;
        }

        private PresetMediaSource MediaSource(bool ffmpeg, bool channel = false)
        {
            var media = new PresetMediaSource(String(), Ffmpeg: ffmpeg, Channel: channel,
                Live: channel, Loop: !channel, HoldLast: !channel, Delay: channel ? .1 : 0);
            if (string.IsNullOrWhiteSpace(media.File)) throw Error("DslUnexpected", media.File);
            if (channel && (media.File.Length > 128 ||
                            media.File.Any(character =>
                                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))))
                throw Error("DslUnexpected", media.File);
            if (!Accept("{")) return media;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!Accept("}"))
            {
                var option = Name();
                if (!seen.Add(option)) throw Error("DslDuplicate", option);
                if (channel && option is not ("delay" or "staleAfter" or "holdLast"))
                    throw Error("DslUnexpected", option);
                switch (option)
                {
                    case "delay" when channel: media = media with { Delay = Numeric() }; break;
                    case "loop": media = media with { Loop = Boolean() }; break;
                    case "speed": media = media with { Speed = Numeric() }; break;
                    case "position": media = media with { Position = Numeric() }; break;
                    case "live": media = media with { Live = Boolean() }; break;
                    case "holdLast": media = media with { HoldLast = Boolean() }; break;
                    case "staleAfter": media = media with { StaleAfter = Numeric() }; break;
                    case "size":
                        Expect("(");
                        var width = Integer(1, 16384);
                        Expect(",");
                        var height = Integer(1, 16384);
                        Expect(")");
                        media = media with { Width = width, Height = height };
                        break;
                    default: throw Error("DslUnexpected", option);
                }

                Expect(";");
            }

            if (!double.IsFinite(media.Speed) || media.Speed < 0 || media.Speed > (media.Ffmpeg ? 16 : 100) ||
                !double.IsFinite(media.StaleAfter) || media.StaleAfter <= 0 ||
                !double.IsFinite(media.Delay) || media.Delay is < 0 or > 5)
                throw Error("DslNumberRequired");
            return media;
        }

        private PresetFallback Fallback()
        {
            Expect("missing");
            var kind = Name();
            var value = kind == "transparent" ? "" : String();
            Expect(";");
            if (kind is not ("transparent" or "color" or "image" or "source")) throw Error("DslUnexpected", kind);
            if (kind == "color") ValidateColor(value);
            return new PresetFallback(kind, value);
        }

        private static bool IsColor(string value) => value == "transparent" ||
                                                     value.Length is 7 or 9 && value[0] == '#' &&
                                                     value[1..].All(char.IsAsciiHexDigit);

        private void ValidateColor(string value)
        {
            if (!IsColor(value)) throw Error("DslColor");
        }

        private bool Boolean() => Accept("true") || Accept("on") ? true :
            Accept("false") || Accept("off") ? false : throw Error("DslBooleanRequired");

        private (bool Client, bool Absolute, IReadOnlyList<PresetExpression> Values) Area()
        {
            Expect("area");
            var absolute = Accept("screen");
            var client = !absolute && (Accept("client") ? true :
                Accept("window") ? false : throw Error("DslExpected", "client/window/screen", Current.Text));
            Expect("(");
            var values = new List<PresetExpression>();
            for (var i = 0; i < 4; i++)
            {
                if (i > 0) Expect(",");
                var e = Expression();
                RequireNumeric(e, !absolute);
                values.Add(e);
            }

            Expect(")");
            Expect(";");
            return (client, absolute, values);
        }

        private double Duration()
        {
            var value = Numeric();
            var multiplier = Accept("ms") ? .001 :
                Accept("s") ? 1 :
                Accept("m") ? 60 : throw Error("DslExpected", "ms/s/m", Current.Text);
            if (value <= 0 || value * multiplier > 86400) throw Error("DslDuration");
            return value * multiplier;
        }

        private PresetRecordingSettings Settings()
        {
            var fields = new Dictionary<string, object?>();
            Expect("{");
            while (!Accept("}"))
            {
                var key = Name();
                if (fields.ContainsKey(key)) throw Error("DslDuplicate", key);
                Accept("=");
                object? value;
                if (Current.Quoted) value = String();
                else if (Is("true") || Is("false") || Is("on") || Is("off")) value = Boolean();
                else if (Current.Text.Length > 0 && (char.IsLetter(Current.Text[0]) || Current.Text[0] == '_') &&
                         !_constants.ContainsKey(Current.Text) && !_values.ContainsKey(Current.Text)) value = Name();
                else
                {
                    var expression = Expression();
                    RequireNumeric(expression, false);
                    value = expression.Evaluate(Sample);
                }

                fields.Add(key, value);
                Expect(";");
            }

            return _recordingSettings.Read(fields);
        }

        private int Integer(int min, int max)
        {
            var e = Expression();
            RequireNumeric(e, false);
            var n = e.Number(Sample);
            if (n < min || n > max || !n.Equals(Math.Truncate(n))) throw Error("DslRange", min, max);
            return (int)n;
        }

        private PresetLayout Layout()
        {
            Expect("layout");
            Expect("(");
            var width = Integer(1, 16384);
            Expect(",");
            var height = Integer(1, 16384);
            Expect(")");
            if ((long)width * height > 64 * 1024 * 1024) throw Error("RecordingTooLarge");
            Expect("{");
            var layers = new List<PresetLayer>();
            var background = "transparent";
            while (!Accept("}"))
            {
                if (Accept("background"))
                {
                    background = String();
                    ValidateColor(background);
                    Expect(";");
                    continue;
                }

                Expect("region");
                var name = String();
                if (layers.Any(l => l.Name == name)) throw Error("DslDuplicate", name);
                var defaults = WindowPreset.ForWindow("*", "*");
                var layer = new PresetLayer(name, null, "*", true, defaults.Area, new PixelRect(0, 0, width, height),
                    new PresetShape("rect"), CaptureSourceKind.Screen, new PresetFallback());
                var seen = new HashSet<string>();
                Expect("{");
                while (!Accept("}"))
                {
                    if (!seen.Add(Current.Text)) throw Error("DslDuplicate", Current.Text);
                    switch (Current.Text)
                    {
                        case "window":
                            var w = Window();
                            layer = layer with { Process = w.Process, Title = w.Title };
                            break;
                        case "source":
                            if (_tokens.Count > _at + 1 && _tokens[_at + 1].Text is "file" or "ffmpeg" or "channel")
                            {
                                Expect("source");
                                var ffmpeg = Accept("ffmpeg");
                                var channel = !ffmpeg && Accept("channel");
                                if (!ffmpeg && !channel) Expect("file");
                                layer = layer with
                                {
                                    Source = CaptureSourceKind.Media, Media = MediaSource(ffmpeg, channel)
                                };
                                Expect(";");
                            }
                            else layer = layer with { Source = Source() };

                            break;
                        case "missing": layer = layer with { Missing = Fallback() }; break;
                        case "follow":
                            _at++;
                            layer = layer with { Follow = Boolean() };
                            Expect(";");
                            break;
                        case "area":
                            var a = Area();
                            layer = layer with { ClientArea = a.Client, Area = a.Values, Absolute = a.Absolute };
                            break;
                        case "place":
                            _at++;
                            Expect("(");
                            var x = Integer(-16384, 16384);
                            Expect(",");
                            var y = Integer(-16384, 16384);
                            Expect(",");
                            var rw = Integer(1, 16384);
                            Expect(",");
                            var rh = Integer(1, 16384);
                            Expect(")");
                            Expect(";");
                            layer = layer with { Placement = new PixelRect(x, y, rw, rh) };
                            break;
                        case "shape":
                            _at++;
                            var kind = Name();
                            double radius = 0;
                            var points = new List<double>();
                            if (kind == "rounded")
                            {
                                Expect("(");
                                radius = Integer(0, 16384);
                                Expect(")");
                            }
                            else if (kind == "polygon")
                            {
                                Expect("(");
                                do
                                {
                                    points.Add(Integer(-16384, 16384));
                                } while (Accept(","));

                                Expect(")");
                                if (points.Count < 6 || points.Count % 2 != 0 || points.Count > 512)
                                    throw Error("DslPolygon");
                            }
                            else if (kind is not ("rect" or "ellipse")) throw Error("DslUnexpected", kind);

                            Expect(";");
                            layer = layer with { Shape = new PresetShape(kind, radius, points) };
                            break;
                        default: throw Error("DslUnexpected", Current.Text);
                    }
                }

                if (layers.Count == 32) throw Error("DslTooComplex");
                if (layer.Absolute && layer.Source != CaptureSourceKind.Screen) throw Error("DslAbsoluteScreen");
                layers.Add(layer);
                if (layers.Sum(l => (long)l.Placement.Width * l.Placement.Height) > 64 * 1024 * 1024)
                    throw Error("RecordingTooLarge");
            }

            if (layers.Count == 0) throw Error("DslLayerRequired");
            return new PresetLayout(width, height, background, layers);
        }

        private PresetState State()
        {
            Expect("state");
            var name = Name();
            Expect("{");
            IReadOnlyList<PresetAction> enter = [], exit = [];
            var rules = new List<PresetRule>();
            var seen = new HashSet<string>();
            while (!Accept("}"))
            {
                if (Is("enter") || Is("exit"))
                {
                    var key = Name();
                    if (!seen.Add(key)) throw Error("DslDuplicate", key);
                    var body = Actions();
                    if (key == "enter") enter = body;
                    else
                    {
                        if (Walk(body).Any(a =>
                                a.Kind is "wait" or "await" or "goto" or "finish" or "call" or "while" or "break"
                                    or "return"))
                            throw Error("DslExitImmediate");
                        exit = body;
                    }
                }
                else rules.Add(Rule());
            }

            return new PresetState(name, enter, exit, rules);
        }

        private PresetRule Rule()
        {
            string kind;
            double delay = 0;
            PresetExpression? condition = null;
            if (Accept("after"))
            {
                kind = "after";
                delay = Duration();
            }
            else if (Accept("when"))
            {
                kind = "when";
                condition = Condition();
            }
            else
            {
                Expect("on");
                kind = Name();
            }

            if (Accept("when")) condition = Condition();
            IReadOnlyList<PresetAction> actions;
            if (Accept("->"))
            {
                actions = [new PresetAction("goto", Name())];
                Expect(";");
            }
            else actions = Actions();

            return new PresetRule(kind, delay, condition, actions);
        }

        private IReadOnlyList<PresetAction> Actions()
        {
            EnterDepth();
            Expect("{");
            var result = new List<PresetAction>();
            while (!Accept("}"))
            {
                var token = Current;
                var first = result.Count;
                var kind = Name();
                switch (kind)
                {
                    case "if":
                        var condition = Condition();
                        var body = Actions();
                        var other = Accept("else") ? Actions() : [];
                        result.Add(new PresetAction(kind, Condition: condition, Body: body, Otherwise: other));
                        break;
                    case "while":
                        var predicate = Condition();
                        _loopDepth++;
                        var loopBody = Actions();
                        _loopDepth--;
                        result.Add(new PresetAction(kind, Condition: predicate, Body: loopBody));
                        break;
                    case "break":
                        if (_loopDepth == 0) throw Error("DslUnexpected", kind);
                        result.Add(new PresetAction(kind));
                        Expect(";");
                        break;
                    case "return":
                        if (_functionDepth == 0) throw Error("DslUnexpected", kind);
                        result.Add(new PresetAction(kind));
                        Expect(";");
                        break;
                    case "log":
                    case "assert":
                    case "emit":
                    case "move":
                    case "visible":
                    case "opacity":
                        var arguments = Arguments();
                        var count = kind == "move" ? 5 : kind is "visible" or "opacity" ? 2 : 1;
                        if (arguments.Count != count) throw Error("DslArguments", kind);
                        if (kind == "assert" && TypeOf(arguments[0]) != typeof(bool)) throw Error("DslBooleanRequired");
                        string? target = null;
                        if (kind is "emit" or "move" or "visible" or "opacity")
                        {
                            target = arguments[0] is PresetLiteral { Value: string text }
                                ? text
                                : throw Error("DslExpected", "string", Current.Text);
                            for (var i = 1; i < arguments.Count; i++)
                                if (TypeOf(arguments[i]) != (kind == "visible" ? typeof(bool) : typeof(double)))
                                    throw Error("DslInvalidExpression");
                        }

                        result.Add(new PresetAction(kind, target, Arguments: arguments));
                        Expect(";");
                        break;
                    case "repeat":
                        var repeatCount = Integer(1, 1000);
                        _loopDepth++;
                        result.Add(new PresetAction(kind, Seconds: repeatCount, Body: Actions()));
                        _loopDepth--;
                        break;
                    case "wait":
                        if (Is("("))
                        {
                            var delay = Arguments();
                            if (delay.Count != 1 || TypeOf(delay[0]) != typeof(double))
                                throw Error("DslArguments", kind);
                            result.Add(new PresetAction(kind, Arguments: delay));
                        }
                        else result.Add(new PresetAction(kind, Seconds: Duration()));

                        Expect(";");
                        break;
                    case "await":
                        var awaited = Condition();
                        Expect("timeout");
                        var timeout = Duration();
                        result.Add(new PresetAction(kind, Seconds: timeout, Condition: awaited));
                        Expect(";");
                        break;
                    case "goto":
                        result.Add(new PresetAction(kind, Name()));
                        Expect(";");
                        break;
                    case "start":
                        EmptyCall();
                        result.Add(new PresetAction(kind, Settings: Is("{") ? Settings() : null));
                        Expect(";");
                        break;
                    case "select":
                    case "stop":
                    case "cancel":
                    case "show":
                    case "hide":
                    case "copy":
                    case "finish":
                    case "breakpoint":
                        EmptyCall();
                        result.Add(new PresetAction(kind));
                        Expect(";");
                        break;
                    default:
                        if (Accept("="))
                        {
                            if (!_types.TryGetValue(kind, out var type)) throw Error("DslUnknownName", kind);
                            var assignment = Expression();
                            if (TypeOf(assignment) != type) throw Error("DslInvalidExpression");
                            result.Add(new PresetAction("assign", kind, Arguments: [assignment]));
                        }
                        else
                        {
                            var args = Arguments();
                            if (args.Any(e => TypeOf(e) != typeof(double))) throw Error("DslNumberRequired");
                            result.Add(new PresetAction("call", kind, Arguments: args));
                        }

                        Expect(";");
                        break;
                }

                for (var i = first; i < result.Count; i++)
                    result[i] = result[i] with { Line = token.Line, Column = token.Column };
            }

            _depth--;
            return result;
        }

        private void EmptyCall()
        {
            if (Accept("(")) Expect(")");
        }

        private IReadOnlyList<PresetExpression> Arguments()
        {
            Expect("(");
            var args = new List<PresetExpression>();
            if (!Accept(")"))
            {
                do
                {
                    args.Add(Expression());
                } while (Accept(","));

                Expect(")");
            }

            return args;
        }

        private void RequireInitial(PresetExpression expression)
        {
            switch (expression)
            {
                case PresetVariable v when !_constants.ContainsKey(v.Name) && !_values.ContainsKey(v.Name):
                    throw Error("DslUnknownName", v.Name);
                case PresetPercent: throw Error("DslInvalidExpression");
                case PresetUnary u: RequireInitial(u.Operand); break;
                case PresetBinary b:
                    RequireInitial(b.Left);
                    RequireInitial(b.Right);
                    break;
                case PresetCall c:
                    foreach (var arg in c.Arguments) RequireInitial(arg);
                    break;
            }
        }

        private PresetFunction Function()
        {
            Expect("fn");
            var name = Name();
            Expect("(");
            var parameters = new List<string>();
            if (!Accept(")"))
            {
                do
                {
                    var parameter = Name();
                    if (Variables.Contains(parameter) || _constants.ContainsKey(parameter) ||
                        _types.ContainsKey(parameter))
                        throw Error("DslDuplicate", parameter);
                    parameters.Add(parameter);
                    _types.Add(parameter, typeof(double));
                } while (Accept(","));

                Expect(")");
            }

            _functionDepth++;
            var body = Actions();
            _functionDepth--;
            foreach (var parameter in parameters) _types.Remove(parameter);
            return new PresetFunction(name, parameters, body);
        }

        private PresetExpression Condition()
        {
            var e = Expression();
            if (TypeOf(e) != typeof(bool)) throw Error("DslBooleanRequired");
            return e;
        }

        private void RequireNumeric(PresetExpression expression, bool geometry)
        {
            if (TypeOf(expression) != typeof(double)) throw Error("DslNumberRequired");

            void Validate(PresetExpression e)
            {
                switch (e)
                {
                    case PresetVariable v
                        when !_constants.ContainsKey(v.Name) && !_values.ContainsKey(v.Name) &&
                             !(geometry && (v.Name is "width" or "height" || _types.ContainsKey(v.Name))):
                        throw Error("DslUnknownName", v.Name);
                    case PresetPercent when !geometry: throw Error("DslInvalidExpression");
                    case PresetCall call:
                        foreach (var arg in call.Arguments) Validate(arg);
                        break;
                    case PresetUnary u: Validate(u.Operand); break;
                    case PresetBinary b:
                        Validate(b.Left);
                        Validate(b.Right);
                        break;
                }
            }

            Validate(expression);
        }

        private Type TypeOf(PresetExpression e) => e switch
        {
            PresetLiteral l => l.Value.GetType(), PresetPercent => typeof(double),
            PresetCall => typeof(double),
            PresetVariable v => _types.TryGetValue(v.Name, out var type) ? type :
                v.Name == "title" ? typeof(string) :
                v.Name is "exists" or "recording" or "busy" or "idle" ? typeof(bool) : typeof(double),
            PresetUnary u => CheckUnary(u), PresetBinary b => CheckBinary(b), _ => throw Error("DslInvalidExpression")
        };

        private Type CheckUnary(PresetUnary e)
        {
            var expected = e.Operator is "not" or "!" ? typeof(bool) : typeof(double);
            if (TypeOf(e.Operand) != expected) throw Error("DslInvalidExpression");
            return expected;
        }

        private Type CheckBinary(PresetBinary e)
        {
            var left = TypeOf(e.Left);
            var right = TypeOf(e.Right);
            if (e.Operator == "+" && left == typeof(string) && right == typeof(string)) return typeof(string);
            var expected = e.Operator is "and" or "or" or "&&" or "||" ? typeof(bool) :
                e.Operator == "~" ? typeof(string) : typeof(double);
            if (e.Operator is "==" or "!=")
            {
                if (left != right) throw Error("DslInvalidExpression");
            }
            else if (left != expected || right != expected) throw Error("DslInvalidExpression");

            return e.Operator is "+" or "-" or "*" or "/" ? typeof(double) : typeof(bool);
        }

        private static int Precedence(string op) => op switch
        {
            "or" or "||" => 1, "and" or "&&" => 2, "==" or "!=" or "~" => 3,
            ">" or "<" or ">=" or "<=" => 4, "+" or "-" => 5, "*" or "/" => 6, _ => 0
        };

        private PresetExpression Expression(int minimum = 1)
        {
            EnterDepth();
            PresetExpression left;
            if (Current.Quoted) left = new PresetLiteral(String());
            else if (Accept("("))
            {
                left = Expression();
                Expect(")");
            }
            else if (Is("not") || Is("!") || Is("-") || Is("+"))
            {
                var op = _tokens[_at++].Text;
                left = new PresetUnary(op, Expression(7));
            }
            else if (Accept("true")) left = new PresetLiteral(true);
            else if (Accept("false")) left = new PresetLiteral(false);
            else if (char.IsAsciiDigit(Current.Text[0]) || Current.Text[0] == '.')
            {
                var n = Numeric();
                left = Accept("%") ? new PresetPercent(n) : new PresetLiteral(n);
            }
            else
            {
                var id = Name();
                if (Is("("))
                {
                    var args = Arguments();
                    var count = id == "clamp" ? 3 :
                        id is "min" or "max" ? 2 :
                        id is "abs" or "round" or "floor" or "ceil" ? 1 : 0;
                    if (count == 0) throw Error("DslUnknownName", id);
                    if (args.Count != count || args.Any(e => TypeOf(e) != typeof(double)))
                        throw Error("DslArguments", id);
                    left = new PresetCall(id, args);
                }
                else
                {
                    if (!_constants.ContainsKey(id) && !_types.ContainsKey(id) && !Variables.Contains(id))
                        throw Error("DslUnknownName", id);
                    left = new PresetVariable(id);
                }
            }

            while (!Current.Quoted && Precedence(Current.Text) is var precedence && precedence >= minimum)
            {
                var op = _tokens[_at++].Text;
                left = new PresetBinary(op, left, Expression(precedence + 1));
            }

            _depth--;
            return left;
        }
    }
}
