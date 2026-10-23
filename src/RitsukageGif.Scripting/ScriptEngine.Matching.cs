namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptEngine
    {
        private static Scope PatternScope(Scope parent, IReadOnlyList<KeyValuePair<string, Cell>>? bindings)
        {
            if (bindings is not { Count: > 0 }) return parent;
            var scope = new Scope(parent);
            foreach (var binding in bindings) scope.DefineCell(binding.Key, binding.Value);
            return scope;
        }

        private static IReadOnlyList<KeyValuePair<string, Cell>>? MergePatternBindings(
            IReadOnlyList<KeyValuePair<string, Cell>>? left, IReadOnlyList<KeyValuePair<string, Cell>>? right)
        {
            if (left is not { Count: > 0 }) return right;
            if (right is not { Count: > 0 }) return left;
            var merged = new List<KeyValuePair<string, Cell>>(left.Count + right.Count);
            merged.AddRange(left);
            foreach (var binding in right)
            {
                if (merged.Any(existing => existing.Key == binding.Key))
                    throw new InvalidOperationException(UiText.Format("DslDuplicate", binding.Key));
                merged.Add(binding);
            }

            return merged;
        }

        private async ValueTask<Evaluation> EvalSwitchAsync(ScriptNode node, Scope scope, CancellationToken token)
        {
            var subject = await EvalAsync(node.Children[0], scope, token);
            if (subject.IsReturn) return subject;
            for (var i = 1; i < node.Children.Count; i++)
            {
                var arm = node.Children[i];
                List<KeyValuePair<string, Cell>>? captures = null;
                if (!MatchPattern(arm.Children[0], subject.Value, ref captures, 0, token)) continue;
                var local = PatternScope(scope, captures);
                var guard = await EvalAsync(arm.Children[1], local, token);
                if (guard.IsReturn) return guard;
                if (!Truth(guard.Value)) continue;
                var result = await EvalAsync(arm.Children[2], PatternScope(local, guard.Bindings), token);
                // Arm-local declarations never become condition bindings outside the switch expression.
                return result with { Bindings = null };
            }

            throw new InvalidOperationException(UiText.Get("ScriptPatternNoMatch"));
        }

        private bool MatchPattern(ScriptNode pattern, object? value,
            ref List<KeyValuePair<string, Cell>>? captures, int depth, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 128) throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
            var checkpoint = captures?.Count ?? 0;
            var matched = MatchPatternCore(pattern, value, ref captures, depth, token);
            if (!matched && captures is not null && captures.Count > checkpoint)
                captures.RemoveRange(checkpoint, captures.Count - checkpoint);
            return matched;
        }

        private bool MatchPatternCore(ScriptNode pattern, object? value,
            ref List<KeyValuePair<string, Cell>>? captures, int depth, CancellationToken token)
        {
            var children = pattern.Children;
            switch (pattern.Kind)
            {
                case "pattern-wildcard": return true;
                case "pattern-binding":
                    captures ??= [];
                    if (captures.Any(existing => existing.Key == pattern.Text))
                        throw new InvalidOperationException(UiText.Format("DslDuplicate", pattern.Text));
                    captures.Add(new KeyValuePair<string, Cell>(pattern.Text, new Cell(value, false, null)));
                    return true;
                case "pattern-literal":
                    return value is not double && pattern.Value is double literal &&
                           _bindings.IsInstanceOf(value, "number")
                        ? Equals(_bindings.Validate(value, "number"), literal)
                        : Equals(value, pattern.Value);
                case "pattern-type":
                    var constructor = _bindings.GetPattern(pattern.Text);
                    if (constructor is not null && children.Count == 0)
                        return constructor.PayloadTypes.Count == 0 &&
                               _bindings.TryMatchPattern(pattern.Text, value, out _);
                    if (!_bindings.IsKnownType(pattern.Text))
                        throw new InvalidOperationException(UiText.Format("DslUnknownName", pattern.Text));
                    return _bindings.IsInstanceOf(value, pattern.Text) &&
                           (children.Count == 0 || MatchPattern(children[0],
                               _bindings.NormalizePattern(value, pattern.Text),
                               ref captures, depth + 1, token));
                case "pattern-constructor":
                    var descriptor = _bindings.GetPattern(pattern.Text) ??
                                     throw new InvalidOperationException(UiText.Format("DslUnknownName", pattern.Text));
                    if (descriptor.PayloadTypes.Count != children.Count) throw TypeError();
                    if (!_bindings.TryMatchPattern(pattern.Text, value, out var fields)) return false;
                    if (fields.Count != children.Count) throw TypeError();
                    for (var i = 0; i < children.Count; i++)
                        if (!MatchPattern(children[i], fields[i], ref captures, depth + 1, token))
                            return false;
                    return true;
                case "pattern-object":
                    if (value is null) return false;
                    foreach (var property in children)
                    {
                        object? field;
                        if (value is ScriptModule module)
                        {
                            if (!module.Names.Contains(property.Text, StringComparer.Ordinal)) return false;
                            field = module.Get(property.Text);
                        }
                        else if (!_bindings.TryReadPatternProperty(value, property.Text, out field)) return false;

                        if (!MatchPattern(property.Children[0], field, ref captures, depth + 1, token)) return false;
                    }

                    return true;
                case "pattern-array": return MatchArrayPattern(pattern, value, ref captures, depth, token);
                case "pattern-relational":
                    if (children[0].Value is not double boundary) return false;
                    if (value is not double && !_bindings.IsInstanceOf(value, "number")) return false;
                    var number = value is double numeric ? numeric : (double)_bindings.Validate(value, "number")!;
                    if (!double.IsFinite(number)) return false;
                    return pattern.Text switch
                    {
                        ">" => number > boundary, ">=" => number >= boundary,
                        "<" => number < boundary, "<=" => number <= boundary, _ => throw TypeError()
                    };
                case "pattern-and":
                    return MatchPattern(children[0], value, ref captures, depth + 1, token) &&
                           MatchPattern(children[1], value, ref captures, depth + 1, token);
                case "pattern-or":
                    return MatchPattern(children[0], value, ref captures, depth + 1, token) ||
                           MatchPattern(children[1], value, ref captures, depth + 1, token);
                case "pattern-not":
                    var checkpoint = captures?.Count ?? 0;
                    var matched = MatchPattern(children[0], value, ref captures, depth + 1, token);
                    if (captures is not null && captures.Count > checkpoint)
                        captures.RemoveRange(checkpoint, captures.Count - checkpoint);
                    return !matched;
                default: throw TypeError();
            }
        }

        private bool MatchArrayPattern(ScriptNode pattern, object? value,
            ref List<KeyValuePair<string, Cell>>? captures, int depth, CancellationToken token)
        {
            var items = value as List<object?>;
            var text = value as string;
            if (items is null && text is null) return false;
            var count = items?.Count ?? text!.Length;
            var children = pattern.Children;
            var rest = -1;
            for (var i = 0; i < children.Count; i++)
                if (children[i].Kind == "pattern-rest")
                {
                    if (rest >= 0) throw TypeError();
                    rest = i;
                }

            if (rest < 0 ? count != children.Count : count < children.Count - 1) return false;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                object? element;
                if (i == rest)
                {
                    child = child.Children[0];
                    if (child.Kind == "pattern-wildcard") continue;
                    var length = count - children.Count + 1;
                    element = items is null ? text!.Substring(i, length) : items.GetRange(i, length);
                }
                else
                {
                    var index = rest >= 0 && i > rest ? count - children.Count + i : i;
                    element = items is null ? text![index].ToString() : items[index];
                }

                if (!MatchPattern(child, element, ref captures, depth + 1, token)) return false;
            }

            return true;
        }
    }
}
