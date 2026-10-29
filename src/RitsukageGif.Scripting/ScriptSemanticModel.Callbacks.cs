using System.Text.RegularExpressions;

namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptSemanticModel
    {
        [GeneratedRegex(@"\$(?:element|receiver\d*|callback\d+|\d+)(?:\?\?\$(?:element|receiver\d*|callback\d+|\d+))?")]
        private static partial Regex InferenceTemplateRegex();

        private Value InferNativeCall(ScriptCallableDescriptor callable, Value? receiver,
            IReadOnlyList<ScriptNode> children, Scope scope, int depth)
        {
            if (callable.InvalidatesReceiverElement && receiver?.TypeName == "array" &&
                (scope.Transient || !IsFunctionAnalysisScope(scope))) receiver.Element = new();
            var template = callable.InferredReturnTypeName ?? callable.ReturnTypeName;
            var arguments = InferCallArguments(children, scope, depth, callable.Parameters.Count);
            if (!template.Contains('$') && callable.Parameters.All(p => p.CallbackParameters is null))
                return Typed(template);
            var callbacks = new Dictionary<int, Value>();

            for (var i = 0; i < arguments.Length && i < callable.Parameters.Count; i++)
            {
                if (callable.Parameters[i].CallbackParameters is not { } parameters || i + 1 >= children.Count ||
                    children.Skip(1).Any(c => c.Kind == "spread")) continue;
                var contextual = parameters.Select(p => Typed(Resolve(p))).ToArray();
                var argument = arguments[i];
                if (argument is { Function: { } function, Closure: { } closure })
                {
                    if (function.Parameters is { } patterns)
                        for (var parameter = 0;
                             parameter < patterns.Count && parameter < contextual.Length;
                             parameter++)
                            if (patterns[parameter].TypeName is { } expected &&
                                !_bindings.IsAssignable(contextual[parameter].TypeName, expected))
                                Diagnostic(children[i + 1], UiText.Get("ScriptTypeError") + " (" +
                                                            contextual[parameter].TypeName + " → " + expected + ")");
                    if (!scope.Transient && children[i + 1].Kind == "function")
                        BindFunctionArguments(function, contextual, closure);
                    callbacks[i] = InferFunctionResult(function, closure, contextual, depth + 1);
                }
                else if (argument.Callable is { } native)
                {
                    callbacks[i] = Typed(ResolveInferenceTemplate(
                        native.InferredReturnTypeName ?? native.ReturnTypeName,
                        contextual, argument.Receiver, new Dictionary<int, Value>()));
                }
                else
                {
                    callbacks[i] = new();
                }

                if (callable.Parameters[i].CallbackReturnType is { } contract)
                {
                    var expected = Resolve(contract);
                    if (!_bindings.IsAssignable(callbacks[i].TypeName, expected))
                        Diagnostic(children[i + 1], UiText.Get("ScriptTypeError") + " (" +
                                                    callbacks[i].TypeName + " → " + expected + ")");
                }
            }

            if (template == "$receiver" && receiver is not null) return receiver;
            var result = Resolve(template);
            Value? merged = null;
            foreach (var alternative in TypeAlternatives(result))
            {
                var value = Typed(alternative);
                merged = merged is null ? value : MergePatternValues(merged, value);
            }

            return merged ?? new Value();

            string Resolve(string text)
            {
                return ResolveInferenceTemplate(text, arguments, receiver, callbacks);
            }
        }

        private static string ResolveInferenceTemplate(string template, IReadOnlyList<Value> arguments,
            Value? receiver, IReadOnlyDictionary<int, Value> callbacks)
        {
            if (!template.Contains('$')) return template;

            if (template[0] == '$' &&
                template.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789".AsSpan()) < 0)
                return Resolve(template) ?? "any";
            return InferenceTemplateRegex().Replace(template, match =>
            {
                var parts = match.Value.Split("??");
                return Resolve(parts[0]) ?? (parts.Length > 1 ? Resolve(parts[1]) : null) ?? "any";
            });

            string? Resolve(string token)
            {
                switch (token)
                {
                    case "$element": return receiver?.Element?.TypeName;
                    case "$receiver": return receiver?.TypeName;
                }

                if (token.StartsWith("$receiver", StringComparison.Ordinal) &&
                    int.TryParse(token.AsSpan(9), out var parameter) && receiver is not null &&
                    ScriptBindingRegistry.TryGetGenericType(receiver.TypeName, out _, out var types))
                    return parameter < types.Length ? types[parameter] : null;
                if (token.StartsWith("$callback", StringComparison.Ordinal) &&
                    int.TryParse(token.AsSpan(9), out var callback))
                    return callbacks.GetValueOrDefault(callback)?.TypeName;
                return int.TryParse(token.AsSpan(1), out var index) && index < arguments.Count
                    ? arguments[index].TypeName
                    : null;
            }
        }
    }
}
