using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptEngine
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Scripting.Engine");

        private static readonly ConditionalWeakTable<ScriptNative, ScriptBoundFunction> NativeFunctions = new();

        private static readonly ScriptCallableDescriptor NativeDescriptor = new("native",
            Array.AsReadOnly([new ScriptParameter("arguments", Variadic: true)]));

        private static readonly object NullChain = new();
        private readonly ScriptBindingRegistry _bindings;
        private readonly List<string> _calls = [];
        private readonly PresetDebugger? _debugger;
        private readonly Stack<string> _files = new();

        private readonly Scope _globals;
        private readonly Func<object?, IReadOnlyList<object?>, CancellationToken, ValueTask<object?>> _invoker;
        private readonly HashSet<string> _loading = new(PathComparer);
        private readonly Dictionary<string, ScriptModule> _modules = new(PathComparer);
        private ScriptNode? _current;

        private int _ticks, _depth, _evaluationDepth;

        public ScriptEngine(PresetDebugger? debugger = null, ScriptBindingRegistry? bindings = null)
        {
            _debugger = debugger;
            _bindings = bindings ?? StandardScriptLibrary.CreateBindings(debugger);
            _invoker = InvokeAsync;
            _globals = new(null, _bindings);
            foreach (var global in _bindings.BindGlobals()) _globals.Define(global.Key, global.Value, true);
        }

        public static ScriptNode Compile(string source, string? file = null)
        {
            try
            {
                var program = ScriptParser.Parse(source);
                Logger.ZLogDebug($"Script compiled. CharacterCount={source.Length}");
                return program;
            }
            catch (PresetSyntaxException error)
            {
                Logger.ZLogDebug($"Script compilation rejected. Line={error.Line} Column={error.Column}");
                if (file is null) throw;
                throw new PresetSyntaxException(error.Line, error.Column, error.RawMessage, Path.GetFullPath(file));
            }
        }

        public void Define(string name, object? value, bool readOnly = true)
        {
            _globals.Define(name, value, readOnly);
        }

        public static ScriptNative Native(Func<IReadOnlyList<object?>, object?> callback)
        {
            return (args, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(callback(args));
            };
        }

        public static double Number(object? value)
        {
            return value is double d && double.IsFinite(d) ? d : throw TypeError();
        }

        public static bool Truth(object? value)
        {
            return value is bool b ? b : throw TypeError();
        }

        private static InvalidOperationException TypeError()
        {
            return new(UiText.Get("ScriptTypeError"));
        }

        public static string Format(object? value)
        {
            return FormatValue(value, 0);
        }

        private static string Text(object? value)
        {
            return ScriptValueText.Convert(value);
        }

        private static string FormatValue(object? value, int depth)
        {
            return value switch
            {
                null => "null", bool b => b ? "true" : "false", double d => d.ToString(CultureInfo.InvariantCulture),
                string s => s.Length > 1000 ? s[..1000] + "…" : s,
                List<object?> a => depth >= 2
                    ? "[…]"
                    : "[" + string.Join(", ", a.Take(20).Select(v => FormatValue(v, depth + 1))) +
                      (a.Count > 20 ? ", …" : "") +
                      "]",
                Dictionary<string, object?> d => depth >= 2
                    ? "{…}"
                    : "{" + string.Join(", ", d.Take(20).Select(p => p.Key + ": " + FormatValue(p.Value, depth + 1))) +
                      (d.Count > 20 ? ", …" : "") + "}",
                IScriptCallable or ScriptNative => "fn",
                ScriptTableBinding table => table.Name,
                ScriptModule module => "module {" + string.Join(", ", module.Names) + "}",
                ScriptResult result when depth >= 2 => result.IsOk ? "Ok(…)" : "Err(…)",
                ScriptResult result => (result.IsOk ? "Ok(" : "Err(") + FormatValue(result.Value, depth + 1) + ")",
                ScriptOption { HasValue: true } when depth >= 2 => "Some(…)",
                ScriptOption option => option.HasValue ? "Some(" + FormatValue(option.Value, depth + 1) + ")" : "None",
                Enum enumeration => enumeration.ToString(),
                _ => "object",
            };
        }

        private async ValueTask CheckAsync(ScriptNode node, Scope scope, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _current = node;
            if (++_ticks % 256 == 0) await Task.Yield();
            if (_debugger is not null)
                await CheckDebuggerAsync(node, scope, token);
        }

        private Task CheckDebuggerAsync(ScriptNode node, Scope scope, CancellationToken token)
        {
            return _debugger!.BeforeScriptAsync(node, _files.Peek(), _calls, scope.Snapshot, token);
        }

        private async ValueTask<Completion> ExecuteAsync(ScriptNode node, Scope scope, CancellationToken token)
        {
            while (true)
            {
                if (node.Kind is not ("block" or "empty" or "while" or "for" or "foreach"))
                    await CheckAsync(node, scope, token);
                var children = node.Children;
                switch (node.Kind)
                {
                    case "empty":
                        return default;
                    case "block":
                        var local = new Scope(scope);
                        HoistFunctions(children, local);
                        foreach (var statement in children)
                        {
                            var completion = await ExecuteAsync(statement, local, token);
                            if (completion.Kind != FlowKind.Normal)
                                return completion;
                        }

                        break;
                    case "declare":
                        var declarationValue = await EvalAsync(children[0], scope, token);
                        if (declarationValue.IsReturn)
                            return declarationValue.Completion;
                        var declaration = await BindAsync(node.Binding ?? NamePattern(node.Text),
                            declarationValue.Value, scope, true, node.Value is true, token, node.TypeName);
                        return declaration.Completion;
                    case "functionDeclaration":
                        break;
                    case "expression":
                        return (await EvalAsync(children[0], scope, token)).Completion;
                    case "if":
                        var condition = await EvalAsync(children[0], scope, token);
                        if (condition.IsReturn)
                            return condition.Completion;
                        if (Truth(condition.Value))
                        {
                            node = children[1];
                            scope = PatternScope(scope, condition.Bindings);
                        }
                        else
                        {
                            node = children[2];
                        }

                        continue;
                    case "while":
                        while (true)
                        {
                            await CheckAsync(node, scope, token);
                            var test = await EvalAsync(children[0], scope, token);
                            if (test.IsReturn)
                                return test.Completion;
                            if (!Truth(test.Value))
                                break;
                            var body = await ExecuteAsync(children[1], PatternScope(scope, test.Bindings), token);
                            if (body.Kind == FlowKind.Return)
                                return body;
                            if (body.Kind == FlowKind.Break)
                                break;
                        }

                        break;
                    case "for":
                        var loop = new Scope(scope);
                        var initializer = await ExecuteAsync(children[0], loop, token);
                        if (initializer.Kind != FlowKind.Normal)
                            return initializer;
                        if (children[0].Kind == "declare" && children[0].Value is not true)
                            loop = loop.Clone();
                        while (true)
                        {
                            await CheckAsync(node, loop, token);
                            var test = await EvalAsync(children[1], loop, token);
                            if (test.IsReturn)
                                return test.Completion;
                            if (!Truth(test.Value))
                                break;
                            var conditionScope = PatternScope(loop, test.Bindings);
                            var body = await ExecuteAsync(children[3], conditionScope, token);
                            if (body.Kind == FlowKind.Return)
                                return body;
                            if (body.Kind == FlowKind.Break)
                                break;
                            if (children[0].Kind == "declare")
                                loop = loop.Clone();
                            var update = await EvalAsync(children[2], PatternScope(loop, test.Bindings), token);
                            if (update.IsReturn)
                                return update.Completion;
                        }

                        break;
                    case "foreach":
                        await CheckAsync(node, scope, token);
                        var sequence = await EvalAsync(children[0], scope, token);
                        if (sequence.IsReturn)
                            return sequence.Completion;
                        var items = sequence.Value switch
                        {
                            List<object?> array => array.ToArray(),
                            Dictionary<string, object?> map => map.Keys.Cast<object?>().ToArray(),
                            _ => throw TypeError(),
                        };
                        foreach (var item in items)
                        {
                            await CheckAsync(node, scope, token);
                            var iteration = new Scope(scope);
                            var binding = await BindAsync(node.Binding ?? NamePattern(node.Text), item, iteration, true,
                                node.Value is true, token);
                            if (binding.IsReturn)
                                return binding.Completion;
                            var body = await ExecuteAsync(children[1], iteration, token);
                            if (body.Kind == FlowKind.Return)
                                return body;
                            if (body.Kind == FlowKind.Break)
                                break;
                        }

                        break;
                    case "return":
                        var returned = await EvalAsync(children[0], scope, token);
                        if (!returned.IsReturn)
                            RememberFailure(returned.Value, node);
                        return new(FlowKind.Return, returned.Value);
                    case "break":
                        return new(FlowKind.Break);
                    case "continue":
                        return new(FlowKind.Continue);
                    case "throw":
                        var thrown = await EvalAsync(children[0], scope, token);
                        if (thrown.IsReturn)
                            return thrown.Completion;
                        throw new Thrown(thrown.Value, _files.Peek(), node.Line, node.Column,
                            CaptureFrames(node, _files.Peek()));
                    case "try":
                        return await ExecuteTryAsync(node, scope, token);
                    case "import":
                    case "importNamed":
                    case "importSideEffect":
                        await ImportStatementAsync(node, scope, token);
                        break;
                    default:
                        throw TypeError();
                }

                return default;
            }
        }

        private async ValueTask<Completion> ExecuteTryAsync(ScriptNode node, Scope scope, CancellationToken token)
        {
            Completion completion = default;
            ExceptionDispatchInfo? pending = null;
            try
            {
                try
                {
                    completion = await ExecuteAsync(node.Children[0], scope, token);
                }
                catch (Exception error) when (error is not OperationCanceledException &&
                                              node.Children[1].Kind != "empty")
                {
                    var handler = new Scope(scope);
                    handler.Define(node.Text,
                        error switch
                        {
                            Thrown thrown => thrown.Value,
                            ScriptExecutionException execution => execution.RawMessage,
                            _ => error.Message,
                        });
                    completion = await ExecuteAsync(node.Children[1], handler, token);
                }
            }
            catch (Exception error)
            {
                pending = ExceptionDispatchInfo.Capture(error);
            }

            Completion final;
            try
            {
                final = await ExecuteAsync(node.Children[2], scope, token);
            }
            catch (Exception) when (pending?.SourceException is OperationCanceledException)
            {
                pending.Throw();
                throw;
            }

            if (pending?.SourceException is OperationCanceledException) pending.Throw();
            if (final.Kind != FlowKind.Normal) return final;
            pending?.Throw();
            return completion;
        }

        public async ValueTask<object?> InvokeAsync(object? callable, IReadOnlyList<object?> arguments,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (callable is ScriptNative native)
                callable = NativeFunctions.GetValue(native, static callback => new(NativeDescriptor,
                    callback));
            if (callable is ScriptBoundFunction bound)
                return await _bindings.InvokeAsync(bound, arguments, _invoker, token);
            if (callable is not Function function) throw TypeError();
            if (++_depth > 64)
            {
                _depth--;
                throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
            }

            var name = string.IsNullOrEmpty(function.Node.Text) ? "fn" : function.Node.Text;
            var callerNode = _current;
            var completed = false;
            _callSites.Add(new(_calls.LastOrDefault() ?? "script",
                _files.TryPeek(out var callerFile) ? callerFile : null, callerNode?.Line ?? 1,
                callerNode?.Column ?? 1));
            _calls.Add(name);
            _files.Push(function.File);
            try
            {
                var parameters = function.Node.Parameters ??
                                 [.. ((string[])function.Node.Value!).Select(NamePattern)];
                var scope = new Scope(function.Closure, functionScope: true, returnType: function.Node.TypeName);
                Evaluation binding = default;
                for (var i = 0; i < parameters.Count; i++)
                {
                    var parameter = parameters[i];
                    _current = parameter;
                    var value = parameter.Kind == "binding-rest" ? arguments.Skip(i).ToList() :
                        i < arguments.Count ? arguments[i] : Missing;
                    binding = await BindAsync(parameter, value, scope, true, false, token);
                    if (binding.IsReturn) break;
                }

                var completion = binding.IsReturn
                    ? binding.Completion
                    : await ExecuteAsync(function.Node.Children[0], scope, token);
                if (completion.Kind is FlowKind.Break or FlowKind.Continue) throw TypeError();
                var result = completion.Value;
                scope.ValidatePropagation(result);
                result = function.Node.TypeName is null ? result : _bindings.Validate(result, function.Node.TypeName);
                completed = true;
                return result;
            }
            catch (Exception error) when (error is not (Thrown or OperationCanceledException
                                              or ScriptExecutionException))
            {
                throw new ScriptExecutionException(_current?.Line ?? 1, _current?.Column ?? 1, error.Message, error,
                    function.File, CaptureFrames(_current, function.File));
            }
            finally
            {
                _files.Pop();
                if (completed) _current = callerNode;
                _calls.RemoveAt(_calls.Count - 1);
                _callSites.RemoveAt(_callSites.Count - 1);
                _depth--;
            }
        }

        private async ValueTask<Evaluation> EvalAsync(ScriptNode node, Scope scope, CancellationToken token,
            bool preserveChain = false)
        {
            token.ThrowIfCancellationRequested();
            if (++_evaluationDepth > 128)
            {
                _evaluationDepth--;
                throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
            }

            try
            {
                var result = await EvalCoreAsync(node, scope, token);
                return !result.IsReturn && !preserveChain && ReferenceEquals(result.Value, NullChain)
                    ? default
                    : result;
            }
            catch (Exception error) when
                (error is not (Thrown or OperationCanceledException or ScriptExecutionException))
            {
                throw new ScriptExecutionException(node.Line, node.Column, error.Message, error, _files.Peek(),
                    CaptureFrames(node, _files.Peek()));
            }
            finally
            {
                _evaluationDepth--;
            }
        }

        private async ValueTask<Evaluation> EvalCoreAsync(ScriptNode node, Scope scope, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var children = node.Children;
            switch (node.Kind)
            {
                case "binding-hole": return default;
                case "literal": return new(node.Value);
                case "group": return await EvalAsync(children[0], scope, token);
                case "switch": return await EvalSwitchAsync(node, scope, token);
                case "is":
                    var subject = await EvalAsync(children[0], scope, token);
                    if (subject.IsReturn) return subject;
                    List<KeyValuePair<string, Cell>>? captures = null;
                    var matches = MatchPattern(children[1], subject.Value, ref captures, 0, token);
                    return new(matches, Bindings: matches ? captures : null);
                case "propagate":
                    var propagated = await EvalAsync(children[0], scope, token);
                    if (propagated.IsReturn) return propagated;
                    var unwrapped = scope.Unwrap(propagated.Value);
                    if (unwrapped.IsReturn) RememberFailure(unwrapped.Value, node);
                    return unwrapped;
                case "slice":
                case "optionalSlice": return await SliceAsync(node, scope, token);
                case "name": return new(scope.Get(node.Text));
                case "function": return new(new Function(node, scope, _files.Peek()));
                case "array":
                    var items = new List<object?>(children.Count);
                    foreach (var child in children)
                    {
                        var item = await AppendAsync(items, child, scope, token);
                        if (item.IsReturn) return item;
                    }

                    return new(items);
                case "object":
                    var map = new Dictionary<string, object?>();
                    foreach (var pair in children)
                    {
                        var field = await EvalAsync(pair.Children[0], scope, token);
                        if (field.IsReturn) return field;
                        if (pair.Kind == "spread")
                        {
                            var spread = field.Value;
                            if (spread is ScriptModule module) spread = module.Snapshot();
                            if (spread is not Dictionary<string, object?> source) throw TypeError();
                            foreach (var entry in source) map[entry.Key] = entry.Value;
                        }
                        else
                        {
                            map[pair.Text] = field.Value;
                        }
                    }

                    return new(map);
                case "member":
                case "optionalMember":
                    var receiver = await EvalAsync(children[0], scope, token, true);
                    if (receiver.IsReturn) return receiver;
                    if (ReferenceEquals(receiver.Value, NullChain) ||
                        (receiver.Value is null && node.Kind == "optionalMember"))
                        return new(NullChain);
                    return new(Get(receiver.Value, node.Text));
                case "index":
                case "optionalIndex":
                    var indexed = await EvalAsync(children[0], scope, token, true);
                    if (indexed.IsReturn) return indexed;
                    if (ReferenceEquals(indexed.Value, NullChain) ||
                        (indexed.Value is null && node.Kind == "optionalIndex"))
                        return new(NullChain);
                    var index = await EvalAsync(children[1], scope, token);
                    if (index.IsReturn) return index;
                    return new(Get(indexed.Value, index.Value));
                case "call":
                case "optionalCall":
                    var function = await EvalAsync(children[0], scope, token, true);
                    if (function.IsReturn) return function;
                    if (ReferenceEquals(function.Value, NullChain) ||
                        (function.Value is null && node.Kind == "optionalCall"))
                        return new(NullChain);
                    if (children.Count == 1)
                    {
                        _current = node;
                        return new(await InvokeAsync(function.Value, [], token));
                    }

                    var arguments = new List<object?>(children.Count - 1);
                    for (var i = 1; i < children.Count; i++)
                    {
                        var argument = await AppendAsync(arguments, children[i], scope, token);
                        if (argument.IsReturn) return argument;
                    }

                    _current = node;
                    return new(await InvokeAsync(function.Value, arguments, token));
                case "conditional":
                    var condition = await EvalAsync(children[0], scope, token);
                    if (condition.IsReturn) return condition;
                    return Truth(condition.Value)
                        ? await EvalAsync(children[1], PatternScope(scope, condition.Bindings), token)
                        : await EvalAsync(children[2], scope, token);
                case "unary":
                    var operand = await EvalAsync(children[0], scope, token);
                    if (operand.IsReturn) return operand;
                    return new(node.Text switch
                    {
                        "!" or "not" => !Truth(operand.Value), "-" => -Number(operand.Value),
                        _ => Number(operand.Value),
                    });
                case "binary":
                    var left = await EvalAsync(children[0], scope, token);
                    if (left.IsReturn) return left;
                    switch (node.Text)
                    {
                        case "??": return left.Value is null ? await EvalAsync(children[1], scope, token) : left;
                        case "&&" or "and" when !Truth(left.Value): return new(false);
                        case "||" or "or" when Truth(left.Value): return new(true);
                    }

                    var conjunction = node.Text is "&&" or "and";
                    var right = await EvalAsync(children[1], conjunction ? PatternScope(scope, left.Bindings) : scope,
                        token);
                    if (right.IsReturn) return right;
                    var binary = Binary(node.Text, left.Value, right.Value);
                    return new(binary, Bindings: conjunction && binary is true
                        ? MergePatternBindings(left.Bindings, right.Bindings)
                        : null);
                case "assign":
                case "postfix":
                    var target = children[0];
                    if (target.Kind.StartsWith("binding-", StringComparison.Ordinal))
                    {
                        var assigned = await EvalAsync(children[1], scope, token);
                        if (assigned.IsReturn) return assigned;
                        var binding = await BindAsync(target, assigned.Value, scope, false, false, token);
                        return binding.IsReturn ? binding : assigned;
                    }

                    var name = target.Text;
                    object? container = null, key = null;
                    if (target.Kind is "member" or "index")
                    {
                        var targetValue = await EvalAsync(target.Children[0], scope, token);
                        if (targetValue.IsReturn) return targetValue;
                        container = targetValue.Value;
                        if (target.Kind == "member")
                        {
                            key = name;
                        }
                        else
                        {
                            var targetKey = await EvalAsync(target.Children[1], scope, token);
                            if (targetKey.IsReturn) return targetKey;
                            key = targetKey.Value;
                        }
                    }
                    else if (target.Kind != "name")
                    {
                        throw TypeError();
                    }

                    var old = node.Text == "=" ? null : target.Kind == "name" ? scope.Get(name) : Get(container, key);
                    object? value;
                    if (node.Kind == "postfix")
                    {
                        value = Binary(node.Text == "++" ? "+" : "-", old, 1d);
                    }
                    else
                    {
                        var replacement = await EvalAsync(children[1], scope, token);
                        if (replacement.IsReturn) return replacement;
                        value = replacement.Value;
                        if (node.Text != "=") value = Binary(node.Text[..1], old, value);
                    }

                    if (target.Kind == "name") scope.Set(name, value);
                    else Set(container, key, value);
                    return new(node.Kind == "postfix" ? old : value);
                default: throw TypeError();
            }
        }

        private static object? Binary(string op, object? left, object? right)
        {
            if (op == "+")
            {
                if (left is string || right is string) return Text(left) + Text(right);
                return Finite(Number(left) + Number(right));
            }

            return op switch
            {
                "-" => Finite(Number(left) - Number(right)), "*" => Finite(Number(left) * Number(right)),
                "/" => Number(right) == 0
                    ? throw new DivideByZeroException(UiText.Get("DslDivisionByZero"))
                    : Finite(Number(left) / Number(right)),
                "%" => Number(right) == 0
                    ? throw new DivideByZeroException(UiText.Get("DslDivisionByZero"))
                    : Number(left) % Number(right),
                "==" => Equals(left, right), "!=" => !Equals(left, right), "<" => Number(left) < Number(right),
                ">" => Number(left) > Number(right), "<=" => Number(left) <= Number(right),
                ">=" => Number(left) >= Number(right),
                "&&" or "and" or "||" or "or" => Truth(right), "~" => WindowPreset.Matches(Text(left), Text(right)),
                _ => throw TypeError(),
            };
        }

        private static double Finite(double value)
        {
            return double.IsFinite(value) ? value : throw TypeError();
        }

        private object? Get(object? value, object? key)
        {
            return value is ScriptModule module && key is string name
                ? module.Get(name)
                : _bindings.GetMember(value, key);
        }

        private void Set(object? value, object? key, object? replacement)
        {
            if (value is ScriptModule) throw new InvalidOperationException(UiText.Format("ScriptReadOnly", key ?? ""));
            _bindings.SetMember(value, key, replacement);
        }

        private sealed class Function(ScriptNode node, Scope closure, string file) : IScriptCallable
        {
            public ScriptNode Node { get; } = node;
            public Scope Closure { get; } = closure;
            public string File { get; } = file;
        }

        private enum FlowKind
        {
            Normal,
            Return,
            Break,
            Continue,
        }

        private readonly record struct Completion(FlowKind Kind, object? Value = null);

        private readonly record struct Evaluation(
            object? Value,
            bool IsReturn = false,
            IReadOnlyList<KeyValuePair<string, Cell>>? Bindings = null)
        {
            public Completion Completion => IsReturn ? new Completion(FlowKind.Return, Value) : default;
        }

        private sealed class PropagationContext(string? returnType = null)
        {
            private string? _family;
            public bool HasPropagation => _family is not null;

            public Evaluation Unwrap(object? value)
            {
                if (value is not IScriptOutcome outcome) throw TypeError();
                var family = outcome.Family;
                if (string.IsNullOrEmpty(family) || (_family is not null && _family != family)) throw TypeError();
                if (returnType is not null && returnType is not ("any" or "unknown"))
                {
                    var expected = ScriptBindingRegistry.TryGetGenericType(returnType, out var name, out _)
                        ? name
                        : returnType;
                    if (expected != family) throw TypeError();
                }

                _family = family;
                return outcome.IsSuccess ? new(outcome.Value) : new Evaluation(value, true);
            }

            public void Validate(object? value)
            {
                if (_family is not null && (value is not IScriptOutcome outcome || outcome.Family != _family))
                    throw TypeError();
            }
        }

        private sealed class Thrown(
            object? value,
            string file,
            int line,
            int column,
            IReadOnlyList<ScriptStackFrame> frames) : Exception(Format(value))
        {
            public IReadOnlyList<ScriptStackFrame> Frames { get; } = frames;
            public object? Value { get; } = value;
            public string File { get; } = file;
            public int Line { get; } = line;
            public int Column { get; } = column;
        }
    }
}
