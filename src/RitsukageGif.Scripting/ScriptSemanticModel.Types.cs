namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptSemanticModel
    {
        private static Value Optional(Value value, bool optional)
        {
            if (!optional || value.TypeName is "any" or "null" || value.TypeName.EndsWith('?')) return value;
            var copy = new Value(value.TypeName + "?")
            {
                Callable = value.Callable, Function = value.Function, Closure = value.Closure,
                Description = value.Description, Declaration = value.Declaration, Element = value.Element,
                Result = value.Result, Receiver = value.Receiver,
            };
            foreach (var member in value.Members) copy.Members[member.Key] = member.Value;
            return copy;
        }

        private static Value Typed(string type, int depth = 0)
        {
            if (depth >= 64 || type.Length > 4096) return new();
            type = ScriptBindingRegistry.UnwrapTypeGroup(type);
            var alternatives = TypeAlternatives(type).ToArray();
            Value? element = null;
            foreach (var alternative in alternatives)
            {
                if (alternative == "null") continue;
                var item = alternative == "string" ? new("string") :
                    alternative.EndsWith("[]", StringComparison.Ordinal) ? Typed(alternative[..^2], depth + 1) : null;
                if (item is null) return new(type);
                element = element is null ? item : MergePatternValues(element, item);
            }

            return new(type) { Element = element };
        }

        private void AddBinding(ScriptNode node, string name, Value value, Scope scope, string kind, string? type,
            bool collect = true)
        {
            if (type is not null) value = Typed(type);
            if (!collect && scope.Bindings.TryGetValue(name, out var existing))
            {
                if (existing.DeclaredType is null) existing.Value = value;
                return;
            }

            var declaration = Declaration(node, name, kind);
            scope.Bindings[name] = new(name,
                kind == "parameter" ? "variable" : value.Function is null ? "variable" : "function",
                kind == "parameter" ? scope.Start : Offset(node), declaration, value) { DeclaredType = type };
            if (collect && !scope.Transient) _symbols.Add(declaration);
        }

        private void BindPattern(ScriptNode pattern, Value value, Scope scope, string kind, bool collect = true)
        {
            while (true)
            {
                if (pattern.TypeName is { } type) value = Typed(type);
                switch (pattern.Kind)
                {
                    case "binding-name":
                        AddBinding(pattern, pattern.Text, value, scope, kind, pattern.TypeName, collect); break;
                    case "binding-default":
                        if (value.TypeName is "any" or "null") value = Infer(pattern.Children[1], scope);
                        pattern = pattern.Children[0];
                        continue;
                    case "binding-rest":
                        if (!value.TypeName.EndsWith("[]", StringComparison.Ordinal) &&
                            value.TypeName is not ("array" or "object"))
                            value = new("array") { Element = value };
                        pattern = pattern.Children[0];
                        continue;
                    case "binding-array":
                        foreach (var child in pattern.Children)
                            BindPattern(child,
                                child.Kind == "binding-rest"
                                    ? new("array") { Element = value.Element }
                                    : value.Element ?? new Value(), scope, kind, collect);
                        break;
                    case "binding-object":
                        foreach (var child in pattern.Children)
                            if (child.Kind == "binding-rest")
                                BindPattern(child.Children[0], value, scope, kind, collect);
                            else
                                BindPattern(child.Children[0],
                                    Members(value).GetValueOrDefault(child.Text) ?? new Value(),
                                    scope, kind, collect);

                        break;
                }

                return;
            }
        }

        private static string PatternName(ScriptNode pattern)
        {
            return pattern.Kind switch
            {
                "binding-name" => pattern.Text,
                "binding-default" or "binding-rest" => PatternName(pattern.Children[0]),
                "binding-property" => pattern.Text + ": " + PatternName(pattern.Children[0]),
                "binding-array" => "[" + string.Join(", ", pattern.Children.Select(PatternName)) + "]",
                "binding-object" => "{" + string.Join(", ", pattern.Children.Select(PatternName)) + "}",
                _ => "",
            };
        }

        private ScriptCallableDescriptor? Callable(Value value, string name = "fn")
        {
            if (value.Callable is { } native) return native;
            if (value.Function is not { } function) return null;
            ScriptParameter[] parameters = function.Parameters is { } patterns
                ?
                [
                    .. patterns.Select(pattern => new ScriptParameter(PatternName(pattern),
                        pattern.TypeName ?? "any", pattern.Kind == "binding-default",
                        Variadic: pattern.Kind == "binding-rest")),
                ]
                : [.. ((string[])function.Value!).Select(p => new ScriptParameter(p))];
            return new(function.Text.Length > 0 ? function.Text : name, parameters,
                function.TypeName ?? (value.Closure is { } scope
                    ? InferFunctionResult(function, scope,
                            [
                                .. ((string[])function.Value!)
                                .Select(parameter => scope.Find(parameter, int.MaxValue)?.Value ?? new Value()),
                            ],
                            0)
                        .TypeName
                    : "any"));
        }

        private void Diagnostic(ScriptNode node, string message)
        {
            var diagnostic = new ScriptDiagnostic(node.Line, node.Column, message, _file);
            if (!_diagnostics.Contains(diagnostic)) _diagnostics.Add(diagnostic);
        }

        private void CheckValue(ScriptNode node, string expected, Scope scope)
        {
            var actual = Infer(node, scope);
            if (!_bindings.IsKnownType(expected))
            {
                Diagnostic(node, UiText.Format("DslUnknownName", expected));
                return;
            }

            if (!_bindings.IsAssignable(actual.TypeName, expected))
            {
                Diagnostic(node, UiText.Get("ScriptTypeError") + " (" + actual.TypeName + " → " + expected + ")");
                return;
            }

            var plainExpected = expected.TrimEnd('?');
            if (node.Kind == "array" && plainExpected.EndsWith("[]", StringComparison.Ordinal))
                foreach (var item in node.Children.Where(c => c.Kind != "spread"))
                    CheckValue(item, plainExpected[..^2], scope);
            if (node is { Kind: "literal", Value: string name } &&
                _bindings.GetEnum(expected.TrimEnd('?')) is { } enumeration &&
                !enumeration.Values.ContainsKey(name) && !enumeration.Values.ContainsKey(
                    name.StartsWith(enumeration.Name + ".", StringComparison.Ordinal)
                        ? name[(enumeration.Name.Length + 1)..]
                        : name))
                Diagnostic(node, UiText.Format("DslUnknownName", name));
            if (node.Kind != "object" || _bindings.GetObjectSchema(expected.TrimEnd('?')) is not { } schema) return;
            foreach (var field in node.Children.Where(c => c.Kind != "spread"))
            {
                var property = schema.FirstOrDefault(p => p.Name == field.Text);
                if (property is not null) CheckValue(field.Children[0], property.TypeName, scope);
            }

            if (node.Children.All(c => c.Kind != "spread"))
                foreach (var property in schema.Where(p => !p.Optional && node.Children.All(c => c.Text != p.Name)))
                    Diagnostic(node, UiText.Format("DslArguments", property.Name));
        }

        private void CheckNode(ScriptNode node, ScriptNode? function)
        {
            var scope = _root.At(Offset(node));
            switch (node)
            {
                case { Kind: "function" }:
                    function = node;
                    if (node.TypeName is { } result && !_bindings.IsKnownType(result))
                        Diagnostic(node, UiText.Format("DslUnknownName", result));
                    if (node.Parameters is { } parameters)
                        foreach (var parameter in parameters)
                        {
                            if (parameter.TypeName is { } type && !_bindings.IsKnownType(type))
                                Diagnostic(parameter, UiText.Format("DslUnknownName", type));
                            if (parameter is { Kind: "binding-default", TypeName: { } expected })
                                CheckValue(parameter.Children[1], expected, scope);
                        }

                    break;
                case { Kind: "propagate" }:
                    CheckPropagation(node, function, scope);
                    break;
                case { Kind: "switch" or "conditional" }:
                case { Kind: "binary", Text: "&&" or "and" }:
                    Infer(node, scope);
                    break;
                case { Kind: "switchArm" }:
                    CheckValue(node.Children[1], "boolean", scope);
                    break;
                case { Kind: "declare", TypeName: { } declared }:
                    CheckValue(node.Children[0], declared, scope);
                    break;
                case { Kind: "return" } when function?.TypeName is { } returns:
                    CheckValue(node.Children[0], returns, scope);
                    break;
                case { Kind: "assign" }:
                    switch (node.Children[0])
                    {
                        case { Kind: "name" } when
                            scope.Find(node.Children[0].Text, Offset(node))?.DeclaredType is { } target:
                            CheckValue(node.Children[1], target, scope);
                            break;
                        case { Kind: "index" } when
                            Infer(node.Children[0].Children[0], scope) is
                                { TypeName: var containerType, Element: { } element } &&
                            containerType.TrimEnd('?').EndsWith("[]", StringComparison.Ordinal):
                            CheckValue(node.Children[1], element.TypeName, scope);
                            break;
                    }

                    break;
                case { Kind: "call" or "optionalCall" } when
                    Callable(Infer(node.Children[0], scope)) is { } callable &&
                    node.Children.All(c => c.Kind != "spread"):
                    var count = node.Children.Count - 1;
                    var required = callable.Parameters.Count(p => p is { Optional: false, Variadic: false });
                    if (count < required ||
                        (count > callable.Parameters.Count && callable.Parameters.All(p => !p.Variadic)))
                        Diagnostic(node, UiText.Format("DslArguments", callable.Name));
                    for (var i = 0; i < count && callable.Parameters.Count > 0; i++)
                    {
                        var parameter = callable.Parameters[Math.Min(i, callable.Parameters.Count - 1)];
                        if (i >= callable.Parameters.Count && !parameter.Variadic) break;
                        var expected = parameter.TypeName;
                        if (parameter.Variadic && expected.EndsWith("[]", StringComparison.Ordinal) &&
                            Infer(node.Children[0], scope).Function is not null)
                            expected = expected[..^2];
                        CheckValue(node.Children[i + 1], expected, scope);
                    }

                    break;
            }

            if (node.Kind.StartsWith("pattern-", StringComparison.Ordinal)) CheckMatchPattern(node);

            foreach (var child in node.Children) CheckNode(child, function);
        }
    }
}
