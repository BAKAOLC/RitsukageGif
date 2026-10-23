namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptSemanticModel
    {
        private void CollectPropagatedReturns(ScriptNode node, Scope scope, int depth, ref Value? result)
        {
            if (depth > 24 || node.Kind is "function" or "functionDeclaration") return;
            if (node.Kind == "propagate") AddPropagatedReturn(node, scope, depth, ref result);
            foreach (var child in node.Children) CollectPropagatedReturns(child, scope, depth + 1, ref result);
        }

        private void AddPropagatedReturn(ScriptNode node, Scope scope, int depth, ref Value? result)
        {
            var type = Infer(node.Children[0], scope.At(Offset(node)), depth + 1).TypeName;
            if (!ScriptBindingRegistry.TryGetGenericType(type, out var family, out var arguments) ||
                _bindings.GetGenericTypeBinding(family)?.OutcomeValueParameter is not { } parameter ||
                parameter >= arguments.Length) return;
            arguments[parameter] = "never";
            var value = Typed(family + "<" + string.Join(",", arguments) + ">");
            result = result is null ? value : MergePatternValues(result, value);
        }

        private Value InferFunctionCall(ScriptNode function, Scope closure, IReadOnlyList<ScriptNode> children,
            Scope caller, int depth)
        {
            var arguments = InferCallArguments(children, caller, depth, ((string[])function.Value!).Length);
            var result = InferFunctionResult(function, closure, arguments, depth);
            ApplyInferenceEffects(function, closure, arguments, caller);
            return result;
        }

        private Value[] InferCallArguments(IReadOnlyList<ScriptNode> children, Scope scope, int depth,
            int parameterCount)
        {
            var values = new List<Value>();
            var uncertain = false;
            foreach (var argument in children.Skip(1))
            {
                if (argument.Kind == "spread")
                {
                    var source = argument.Children[0];
                    if (!uncertain && source.Kind == "array" && source.Children.All(c => c.Kind != "spread"))
                        values.AddRange(source.Children.Select(item => Infer(item, scope, depth + 1)));
                    else
                    {
                        Infer(source, scope, depth + 1);
                        uncertain = true;
                        values.Add(new Value());
                    }
                }
                else
                {
                    var value = Infer(argument, scope, depth + 1);
                    values.Add(uncertain ? new Value() : value);
                }
            }

            if (uncertain)
                while (values.Count < parameterCount)
                    values.Add(new Value());
            return values.ToArray();
        }

        private Value InferFunctionResult(ScriptNode function, Scope closure, IReadOnlyList<Value> arguments, int depth)
        {
            if (depth > 24) return new Value();
            if (function.TypeName is { } returnType) return Typed(returnType);
            var scope = CopyInferenceScope(closure.Parent, InferenceWrites(function),
                rootReferences: InferenceReferences(function));
            var invocation = new Scope(closure.Start, closure.End, scope) { Transient = true };
            var copies = new Dictionary<Value, Value>(ReferenceEqualityComparer.Instance);
            BindFunctionArguments(function, arguments.Select(value => CopyInferenceValue(value, copies)).ToArray(),
                invocation);
            Value? result = null;
            InferFlow(function.Children[0], invocation, depth + 1, ref result);
            return AlwaysReturns(function.Children[0])
                ? result ?? Typed("never")
                : MergePatternValues(result ?? Typed("never"), Typed("null"));
        }

        private static IEnumerable<string> TypeAlternatives(string type)
        {
            type = ScriptBindingRegistry.UnwrapTypeGroup(type);
            var nesting = 0;
            var start = 0;
            for (var i = 0; i <= type.Length; i++)
            {
                if (i < type.Length && type[i] is '<' or '(' or '[') nesting++;
                if (i < type.Length && type[i] is '>' or ')' or ']') nesting--;
                if (i < type.Length && (type[i] != '|' || nesting != 0)) continue;
                var item = type[start..i].Trim();
                if (item.EndsWith('?'))
                {
                    foreach (var alternative in TypeAlternatives(item[..^1])) yield return alternative;
                    yield return "null";
                }
                else if (ScriptBindingRegistry.UnwrapTypeGroup(item) is var unwrapped && unwrapped != item)
                    foreach (var alternative in TypeAlternatives(unwrapped))
                        yield return alternative;
                else yield return item;

                start = i + 1;
            }
        }

        private static Value MergePatternValues(Value left, Value right, int depth = 0)
        {
            if (ReferenceEquals(left, right)) return left;
            if (depth > 24) return new Value();
            if (left.TypeName == "never") return right;
            if (right.TypeName == "never") return left;
            var types = TypeAlternatives(left.TypeName).Concat(TypeAlternatives(right.TypeName))
                .Where(t => t != "never").Distinct(StringComparer.Ordinal).ToArray();
            var type = types.Contains("any", StringComparer.Ordinal) ? "any" :
                types.Contains("unknown", StringComparer.Ordinal) ? "unknown" : string.Join("|", types);
            if (ScriptBindingRegistry.TryGetGenericType(left.TypeName, out var family, out var leftArguments) &&
                ScriptBindingRegistry.TryGetGenericType(right.TypeName, out var rightFamily, out var rightArguments) &&
                family == rightFamily && leftArguments.Length == rightArguments.Length)
                type = family + "<" + string.Join(",", leftArguments.Zip(rightArguments,
                    (a, b) => MergePatternValues(Typed(a), Typed(b), depth + 1).TypeName)) + ">";
            var merged = new Value(type)
            {
                Element = left.Element is { } leftElement && right.Element is { } rightElement
                    ? MergePatternValues(leftElement, rightElement, depth + 1)
                    : null,
                Callable = left.Callable == right.Callable ? left.Callable : null,
                Function = ReferenceEquals(left.Function, right.Function) ? left.Function : null,
                Closure = ReferenceEquals(left.Closure, right.Closure) ? left.Closure : null,
                Receiver = ReferenceEquals(left.Receiver, right.Receiver) ? left.Receiver : null
            };
            foreach (var member in left.Members)
                if (right.Members.TryGetValue(member.Key, out var other))
                    merged.Members[member.Key] = MergePatternValues(member.Value, other, depth + 1);
            return merged;
        }
    }
}
