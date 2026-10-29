namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptSemanticModel
    {
        private readonly HashSet<(ScriptNode Pattern, Scope Scope)> _boundMatchPatterns = [];
        private readonly HashSet<(ScriptNode Switch, string Type)> _checkedSwitchCoverage = [];
        private readonly Dictionary<ScriptNode, Scope> _patternScopes = new(ReferenceEqualityComparer.Instance);
        private int _patternComparisonBudget;

        private int PatternNodeStart(ScriptNode node)
        {
            return node.Children.Count == 0
                ? Offset(node)
                : Math.Min(Offset(node), node.Children.Min(PatternNodeStart));
        }

        private int PatternNodeEnd(ScriptNode node)
        {
            if (node.Kind == "block") return _blockEnds.GetValueOrDefault(Offset(node), _source.Length);
            var offset = Offset(node);
            var end = _tokens.FirstOrDefault(t => t.Offset == offset) is { } token
                ? token.Offset + token.Text.Length
                : offset;
            foreach (var child in node.Children) end = Math.Max(end, PatternNodeEnd(child));
            if (node.Kind is "member" or "optionalMember" &&
                _tokens.FirstOrDefault(t => t.Offset >= end && t.Text == node.Text) is { } member)
                end = member.Offset + member.Length;
            return end;
        }

        private Scope PatternScope(ScriptNode node, Scope parent, int end)
        {
            if (!parent.Transient && _patternScopes.TryGetValue(node, out var existing)) return existing;
            var scope = new Scope(PatternNodeStart(node), end, parent);
            parent.Children.Add(scope);
            if (!parent.Transient) _patternScopes[node] = scope;
            return scope;
        }

        private Scope PositiveScope(ScriptNode condition, ScriptNode body, Scope parent, int? end = null)
        {
            var scope = PatternScope(body, parent, end ?? PatternNodeEnd(body));
            BindPositiveCondition(condition, scope, parent);
            return scope;
        }

        private void BuildPositiveBranch(ScriptNode condition, ScriptNode body, Scope scope)
        {
            Build(body, PositiveScope(condition, body, scope));
        }

        private void BindPositiveCondition(ScriptNode condition, Scope target, Scope source)
        {
            while (true)
                switch (condition)
                {
                    case { Kind: "group" }:
                        condition = condition.Children[0];
                        continue;
                    case { Kind: "is" }:
                        BindMatchPattern(condition.Children[1], Infer(condition.Children[0], source), target);
                        return;
                    case { Kind: "binary", Text: "&&" or "and" }:
                        BindPositiveCondition(condition.Children[0], target, source);
                        condition = condition.Children[1];
                        source = target;
                        continue;
                    default: return;
                }
        }

        private Value InferSwitch(ScriptNode node, Scope parent, int depth)
        {
            var input = Infer(node.Children[0], parent, depth + 1);
            CheckSwitchCoverage(node, input);
            Value? result = null;
            for (var i = 1; i < node.Children.Count; i++)
            {
                var arm = node.Children[i];
                var end = i + 1 < node.Children.Count ? Offset(node.Children[i + 1]) - 1 : PatternNodeEnd(arm);
                var scope = PatternScope(arm, parent, end);
                BindMatchPattern(arm.Children[0], input, scope);
                Infer(arm.Children[1], scope, depth + 1);
                var value = Infer(arm.Children[2], PositiveScope(arm.Children[1], arm.Children[2], scope), depth + 1);
                result = result is null ? value : MergePatternValues(result, value);
            }

            return result ?? new Value();
        }

        private void BindMatchPattern(ScriptNode pattern, Value input, Scope scope)
        {
            while (true)
            {
                if (!scope.Transient && !_boundMatchPatterns.Add((pattern, scope))) return;
                switch (pattern.Kind)
                {
                    case "pattern-binding":
                        if (!scope.Bindings.ContainsKey(pattern.Text))
                            AddBinding(pattern, pattern.Text, input, scope, "let", null);
                        break;
                    case "pattern-type":
                        if (pattern.Children.Count == 0) return;
                        input = Typed(pattern.Text);
                        pattern = pattern.Children[0];
                        continue;
                    case "pattern-constructor":
                        var descriptor = _bindings.GetPattern(pattern.Text);
                        ScriptBindingRegistry.TryGetGenericType(input.TypeName, out _, out var arguments);
                        for (var i = 0; i < pattern.Children.Count; i++)
                        {
                            var type = descriptor is not null && i < descriptor.PayloadTypes.Count
                                ? descriptor.PayloadTypes[i]
                                : "any";
                            type = GenericParameterRegex().Replace(type, match =>
                                int.TryParse(match.Groups[1].Value, out var index) && index < arguments.Length
                                    ? arguments[index]
                                    : "any");
                            BindMatchPattern(pattern.Children[i], Typed(type), scope);
                        }

                        break;
                    case "pattern-object":
                        foreach (var property in pattern.Children)
                            BindMatchPattern(property.Children[0],
                                Members(input).GetValueOrDefault(property.Text) ?? new Value(), scope);
                        break;
                    case "pattern-array":
                        foreach (var item in pattern.Children)
                            BindMatchPattern(item,
                                item.Kind == "pattern-rest" ? input :
                                input.TypeName == "string" ? Typed("string") : input.Element ?? new Value(),
                                scope);
                        break;
                    case "pattern-rest":
                        input = input.TypeName == "any" ? new("array") : input;
                        pattern = pattern.Children[0];
                        continue;
                    case "pattern-and":
                        foreach (var child in pattern.Children) BindMatchPattern(child, input, scope);
                        break;
                    case "pattern-or":
                        var left = new Scope(scope.Start, scope.End, scope.Parent);
                        var right = new Scope(scope.Start, scope.End, scope.Parent);
                        BindMatchPattern(pattern.Children[0], input, left);
                        BindMatchPattern(pattern.Children[1], input, right);
                        foreach (var binding in left.Bindings)
                            if (right.Bindings.TryGetValue(binding.Key, out var other))
                            {
                                binding.Value.Value = MergePatternValues(binding.Value.Value, other.Value);
                                scope.Bindings[binding.Key] = binding.Value;
                            }

                        break;
                }

                return;
            }
        }

        private static bool IsTotalPattern(ScriptNode pattern)
        {
            return pattern.Kind is "pattern-wildcard" or "pattern-binding" ||
                   (pattern.Kind == "pattern-or" && pattern.Children.Any(IsTotalPattern)) ||
                   (pattern.Kind == "pattern-and" && pattern.Children.All(IsTotalPattern));
        }

        private IEnumerable<string> CoveredPatternCases(ScriptNode pattern)
        {
            switch (pattern)
            {
                case { Kind: "pattern-or" }: return pattern.Children.SelectMany(CoveredPatternCases);
                case { Kind: "pattern-constructor" } when pattern.Children.All(IsTotalPattern):
                case { Kind: "pattern-type", Children.Count: 0 } when
                    _bindings.GetPattern(pattern.Text) is { PayloadTypes.Count: 0 }:
                    return [pattern.Text];
                default: return [];
            }
        }

        private void CheckSwitchCoverage(ScriptNode node, Value input)
        {
            if (!_checkedSwitchCoverage.Add((node, input.TypeName))) return;
            _patternComparisonBudget = 4096;
            var family = ScriptBindingRegistry.TryGetGenericType(input.TypeName, out var type, out _)
                ? type
                : input.TypeName;
            var cases = _bindings.GetPatternCases(family);
            var covered = new HashSet<string>(StringComparer.Ordinal);
            var preceding = new List<ScriptNode>();
            var total = false;
            foreach (var arm in node.Children.Skip(1))
            {
                var pattern = arm.Children[0];
                if (total || (_patternComparisonBudget > 0 &&
                              preceding.Any(previous => PatternSubsumes(previous, pattern))))
                    Diagnostic(pattern, UiText.Get("ScriptPatternUnreachable"));
                if (arm.Children[1] is not { Kind: "literal", Value: true }) continue;
                total |= IsTotalPattern(pattern) || (pattern.Kind == "pattern-type" &&
                                                     (pattern.Children.Count > 0 ||
                                                      _bindings.GetPattern(pattern.Text) is null) &&
                                                     (pattern.Text is "any" or "unknown" ||
                                                      (pattern.Text == input.TypeName &&
                                                       _bindings.IsKnownType(pattern.Text))));
                preceding.Add(pattern);
                covered.UnionWith(CoveredPatternCases(pattern));
                if (cases is not null && cases.All(covered.Contains)) total = true;
            }

            if (!total && cases is not null) Diagnostic(node, UiText.Get("ScriptPatternIncomplete"));
        }

        private bool PatternSubsumes(ScriptNode previous, ScriptNode current)
        {
            if (--_patternComparisonBudget < 0) return false;
            if (IsTotalPattern(previous)) return true;
            switch (previous.Kind, current.Kind)
            {
                case (_, "pattern-or"): return current.Children.All(child => PatternSubsumes(previous, child));
                case ("pattern-or", _): return previous.Children.Any(child => PatternSubsumes(child, current));
                case ("pattern-and", _): return previous.Children.All(child => PatternSubsumes(child, current));
                case (_, "pattern-and"): return current.Children.Any(child => PatternSubsumes(previous, child));
            }

            switch (previous.Kind)
            {
                case "pattern-type":
                    if (_bindings.IsKnownType(previous.Text) &&
                        (previous.Children.Count > 0 || _bindings.GetPattern(previous.Text) is null))
                        switch (current)
                        {
                            case { Kind: "pattern-type" } when current.Text == previous.Text: return true;
                            case { Kind: "pattern-literal" }:
                                return _bindings.IsInstanceOf(current.Value, previous.Text);
                            case { Kind: "pattern-relational" }: return previous.Text is "number" or "any" or "unknown";
                        }

                    return current is { Kind: "pattern-type", Children.Count: 0 } &&
                           previous.Text == current.Text && previous.Children.Count == 0;
                case "pattern-literal":
                    return current.Kind == "pattern-literal" && Equals(previous.Value, current.Value);
                case "pattern-relational":
                    if (previous.Children[0].Value is not double boundary) return false;
                    return current switch
                    {
                        { Kind: "pattern-literal", Value: double value } => previous.Text switch
                        {
                            ">" => value > boundary, ">=" => value >= boundary,
                            "<" => value < boundary, "<=" => value <= boundary, _ => false,
                        },
                        { Kind: "pattern-relational" } when current.Children[0].Value is double other =>
                            (previous.Text, current.Text) switch
                            {
                                (">", ">") or (">=", ">") or (">=", ">=") => other >= boundary,
                                (">", ">=") => other > boundary,
                                ("<", "<") or ("<=", "<") or ("<=", "<=") => other <= boundary,
                                ("<", "<=") => other < boundary,
                                _ => false,
                            },
                        _ => false,
                    };
                case "pattern-constructor":
                    return current.Kind == "pattern-constructor" && previous.Text == current.Text &&
                           previous.Children.Count == current.Children.Count &&
                           previous.Children.Zip(current.Children)
                               .All(pair => PatternSubsumes(pair.First, pair.Second));
                case "pattern-object":
                    return current.Kind == "pattern-object" &&
                           previous.Children.All(property => current.Children.Any(other =>
                               property.Text == other.Text &&
                               PatternSubsumes(property.Children[0], other.Children[0])));
                case "pattern-array":
                    return current.Kind == "pattern-array" && previous.Children.Count == current.Children.Count &&
                           previous.Children.All(child => child.Kind != "pattern-rest") &&
                           current.Children.All(child => child.Kind != "pattern-rest") &&
                           previous.Children.Zip(current.Children)
                               .All(pair => PatternSubsumes(pair.First, pair.Second));
                default: return false;
            }
        }

        private void CheckMatchPattern(ScriptNode node)
        {
            if (node.Kind == "pattern-type" && !_bindings.IsKnownType(node.Text) &&
                !(node.Children.Count == 0 && _bindings.GetPattern(node.Text) is { PayloadTypes.Count: 0 }))
                Diagnostic(node, UiText.Format("DslUnknownName", node.Text));
            if (node.Kind != "pattern-constructor") return;
            var descriptor = _bindings.GetPattern(node.Text);
            if (descriptor is null) Diagnostic(node, UiText.Format("DslUnknownName", node.Text));
            else if (descriptor.PayloadTypes.Count != node.Children.Count)
                Diagnostic(node, UiText.Format("DslArguments", node.Text));
        }
    }
}
