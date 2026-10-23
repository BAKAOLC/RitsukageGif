namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptSemanticModel
    {
        private readonly Dictionary<ScriptNode, HashSet<string>> _inferenceWrites =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<ScriptNode, HashSet<string>> _inferenceReferences =
            new(ReferenceEqualityComparer.Instance);

        private HashSet<string> InferenceReferences(ScriptNode function)
        {
            if (_inferenceReferences.TryGetValue(function, out var names)) return names;
            names = new HashSet<string>(StringComparer.Ordinal);

            void Visit(ScriptNode node)
            {
                if (node.Kind == "name") names.Add(node.Text);
                foreach (var child in node.Children) Visit(child);
                if (node.Binding is { } binding) Visit(binding);
                if (node.Parameters is { } parameters)
                    foreach (var parameter in parameters)
                        Visit(parameter);
            }

            Visit(function);
            _inferenceReferences[function] = names;
            return names;
        }

        private HashSet<string> InferenceWrites(ScriptNode function)
        {
            if (_inferenceWrites.TryGetValue(function, out var names)) return names;
            names = new HashSet<string>(StringComparer.Ordinal);

            void Visit(ScriptNode node)
            {
                if (node.Kind is "function" or "functionDeclaration") return;
                if (node.Kind is "assign" or "postfix" or "call" or "optionalCall")
                {
                    var target = node.Children[0];
                    while (target.Kind is "index" or "member" or "optionalMember") target = target.Children[0];
                    if (target.Kind == "name") names.Add(target.Text);
                }

                foreach (var child in node.Children) Visit(child);
                if (node.Binding is { } binding) Visit(binding);
            }

            Visit(function.Children[0]);
            if (function.Parameters is { } parameters)
                foreach (var parameter in parameters)
                    Visit(parameter);
            _inferenceWrites[function] = names;
            return names;
        }

        private static Scope? CopyInferenceScope(Scope? source, IReadOnlySet<string>? rootWrites = null,
            Dictionary<Value, Value>? copies = null, IReadOnlySet<string>? rootReferences = null)
        {
            if (source is null) return null;
            copies ??= new Dictionary<Value, Value>(ReferenceEqualityComparer.Instance);
            var root = source.Parent is null && !source.Transient;
            var copy = new Scope(source.Start, source.End,
                    root && rootWrites is not null
                        ? source
                        : CopyInferenceScope(source.Parent, rootWrites, copies, rootReferences))
                { Transient = true };
            foreach (var pair in source.Bindings)
            {
                if (root && rootWrites is not null && !rootWrites.Contains(pair.Key) &&
                    (rootReferences is not null && !rootReferences.Contains(pair.Key) ||
                     pair.Value.Value.Element is null && pair.Value.Value.Members.Count == 0)) continue;
                var binding = pair.Value;
                copy.Bindings[pair.Key] = new Binding(binding.Name, binding.Kind, binding.Offset,
                        binding.Declaration, CopyInferenceValue(binding.Value, copies))
                    { DeclaredType = binding.DeclaredType };
            }

            return copy;
        }

        private static Value CopyInferenceValue(Value value, Dictionary<Value, Value> copies, int depth = 0)
        {
            if (value.Element is null && value.Members.Count == 0 || depth > 24) return value;
            if (copies.TryGetValue(value, out var existing)) return existing;
            var copy = new Value(value.TypeName)
            {
                Callable = value.Callable, Function = value.Function, Closure = value.Closure,
                Description = value.Description, Declaration = value.Declaration, Receiver = value.Receiver,
                Result = value.Result
            };
            copies[value] = copy;
            if (value.Element is { } element) copy.Element = CopyInferenceValue(element, copies, depth + 1);
            foreach (var member in value.Members)
                copy.Members[member.Key] = CopyInferenceValue(member.Value, copies, depth + 1);
            return copy;
        }

        private Value InferenceFunction(ScriptNode function, Scope parent)
        {
            var body = function.Children[0];
            return new Value("function")
            {
                Function = function,
                Closure = new Scope(Offset(body), BlockEnd(body, parent.End), parent) { Transient = true }
            };
        }

        private void BindFunctionArguments(ScriptNode function, IReadOnlyList<Value> arguments, Scope scope)
        {
            var names = (string[])function.Value!;
            for (var i = 0; i < names.Length; i++)
            {
                var argument = i < arguments.Count ? arguments[i] : Typed("null");
                if (function.Parameters is { } patterns)
                {
                    if (patterns[i].Kind == "binding-rest")
                    {
                        Value? element = null;
                        for (var j = i; j < arguments.Count; j++)
                            element = element is null ? arguments[j] : MergePatternValues(element, arguments[j]);
                        argument = new Value("array") { Element = element ?? Typed("never") };
                    }

                    BindPattern(patterns[i], argument, scope, "parameter", false);
                }
                else AddBinding(function, names[i], argument, scope, "parameter", null, false);
            }
        }

        private void InferFlow(ScriptNode node, Scope scope, int depth, ref Value? result)
        {
            if (depth > 24)
            {
                result = new Value();
                return;
            }

            var children = node.Children;
            switch (node.Kind)
            {
                case "block":
                    var block = new Scope(Offset(node), BlockEnd(node, scope.End), scope) { Transient = true };
                    foreach (var declaration in children.Where(n => n.Kind == "functionDeclaration"))
                        block.Bindings[declaration.Text] = new Binding(declaration.Text, "function", block.Start,
                            Declaration(declaration, declaration.Text, "fn"),
                            InferenceFunction(declaration.Children[0], block));
                    foreach (var child in children)
                    {
                        InferFlow(child, block, depth + 1, ref result);
                        if (AlwaysReturns(child)) break;
                    }

                    return;
                case "declare":
                    var value = Infer(children[0], scope, depth + 1);
                    if (node.Binding is { } pattern) BindPattern(pattern, value, scope, "let", false);
                    else AddBinding(node, node.Text, value, scope, "let", node.TypeName, false);
                    CollectPropagatedReturns(children[0], scope, depth + 1, ref result);
                    return;
                case "return":
                    var returned = children.Count == 0 ? Typed("null") : Infer(children[0], scope, depth + 1);
                    result = result is null ? returned : MergePatternValues(result, returned);
                    foreach (var child in children) CollectPropagatedReturns(child, scope, depth + 1, ref result);
                    return;
                case "if":
                    CollectPropagatedReturns(children[0], scope, depth + 1, ref result);
                    var positive = CopyInferenceScope(scope)!;
                    BindPositiveCondition(children[0], positive, positive);
                    InferFlow(children[1], positive, depth + 1, ref result);
                    var negative = CopyInferenceScope(scope)!;
                    InferFlow(children[2], negative, depth + 1, ref result);
                    MergeInferenceScopes(scope, AlwaysReturns(children[1]) ? null : positive,
                        AlwaysReturns(children[2]) ? null : negative);
                    return;
                case "while":
                case "for":
                case "foreach":
                    var beforeLoop = CopyInferenceScope(scope)!;
                    var loop = new Scope(Offset(node), BlockEnd(node, scope.End), beforeLoop) { Transient = true };
                    if (node.Kind == "for") InferFlow(children[0], loop, depth + 1, ref result);
                    var stable = false;
                    for (var iteration = 0; iteration < 4; iteration++)
                    {
                        var previous = InferenceState(beforeLoop);
                        if (node.Kind == "foreach")
                        {
                            var item = Infer(children[0], loop, depth + 1).Element ?? new Value();
                            if (node.Binding is { } binding) BindPattern(binding, item, loop, "let", false);
                            else AddBinding(node, node.Text, item, loop, "let", null, false);
                            InferFlow(children[1], loop, depth + 1, ref result);
                        }
                        else
                        {
                            var condition = node.Kind == "for" ? children[1] : children[0];
                            BindPositiveCondition(condition, loop, loop);
                            InferFlow(node.Kind == "for" ? children[3] : children[1], loop, depth + 1, ref result);
                            if (node.Kind == "for") Infer(children[2], loop, depth + 1);
                        }

                        MergeInferenceScopes(beforeLoop, scope, beforeLoop);
                        if (previous == InferenceState(beforeLoop))
                        {
                            stable = true;
                            break;
                        }
                    }

                    if (!stable)
                        for (Scope? current = beforeLoop; current is { Transient: true }; current = current.Parent)
                            foreach (var binding in current.Bindings.Values)
                                if (binding.DeclaredType is null)
                                    binding.Value = new Value();
                    MergeInferenceScopes(scope, scope, beforeLoop);
                    return;
                case "try":
                    if (AlwaysReturns(children[2]))
                    {
                        InferFlow(children[2], scope, depth + 1, ref result);
                        return;
                    }

                    var attempted = CopyInferenceScope(scope)!;
                    InferFlow(children[0], attempted, depth + 1, ref result);
                    var caught = CopyInferenceScope(scope)!;
                    if (node.Text.Length > 0) AddBinding(node, node.Text, new Value(), caught, "let", null, false);
                    InferFlow(children[1], caught, depth + 1, ref result);
                    MergeInferenceScopes(scope, attempted, children[1].Kind == "empty" ? null : caught);
                    InferFlow(children[2], scope, depth + 1, ref result);
                    return;
                case "expression":
                    var expression = children[0];
                    Infer(expression, scope, depth + 1);
                    CollectPropagatedReturns(expression, scope, depth + 1, ref result);
                    return;
                case "functionDeclaration": return;
            }

            foreach (var child in children) CollectPropagatedReturns(child, scope, depth + 1, ref result);
        }

        private static bool IsFunctionAnalysisScope(Scope scope)
        {
            for (var parent = scope; parent is not null; parent = parent.Parent)
                if (parent.FunctionScope)
                    return true;
            return false;
        }

        private void ApplyInferenceEffects(ScriptNode function, Scope closure, IReadOnlyList<Value> arguments,
            Scope caller)
        {
            if (!caller.Transient && IsFunctionAnalysisScope(caller)) return;
            var parameters = (string[])function.Value!;
            var locals = new HashSet<string>(parameters, StringComparer.Ordinal);
            var assigned = new HashSet<string>(StringComparer.Ordinal);

            void CollectLocals(ScriptNode node)
            {
                if (node.Kind is "function" or "functionDeclaration") return;
                if (node.Kind == "declare" && node.Text.Length > 0) locals.Add(node.Text);
                if (node.Kind is "assign" or "postfix")
                {
                    var target = node.Children[0];
                    while (target.Kind is "index" or "member") target = target.Children[0];
                    if (target.Kind == "name") assigned.Add(target.Text);
                }

                foreach (var child in node.Children) CollectLocals(child);
            }

            CollectLocals(function.Children[0]);
            foreach (var name in InferenceWrites(function))
            {
                var parameter = Array.IndexOf(parameters, name);
                if (parameter >= 0 && parameter < arguments.Count)
                {
                    if (arguments[parameter].TypeName == "array") arguments[parameter].Element = new Value();
                    if (arguments[parameter].TypeName == "object") arguments[parameter].Members.Clear();
                    continue;
                }

                if (locals.Contains(name) || closure.Parent?.Find(name, int.MaxValue) is not { } captured ||
                    captured.Value.TypeName == "function") continue;
                if (captured.DeclaredType is not null || !assigned.Contains(name) &&
                    captured.Value.Element is null && captured.Value.Members.Count == 0) continue;
                if (caller.Transient)
                {
                    Binding? local = null;
                    Scope? last = null;
                    for (var current = caller; current is { Transient: true }; current = current.Parent)
                    {
                        last = current;
                        if (current.Bindings.TryGetValue(name, out var candidate) &&
                            candidate.Declaration == captured.Declaration)
                        {
                            local = candidate;
                            break;
                        }
                    }

                    if (local is null && last is not null)
                    {
                        local = new Binding(captured.Name, captured.Kind, captured.Offset, captured.Declaration,
                            new Value());
                        last.Bindings[name] = local;
                    }

                    if (local is not null) local.Value = new Value();
                }
                else captured.Value = new Value();
            }
        }

        private static string InferenceState(Scope scope)
        {
            var state = new System.Text.StringBuilder();
            for (Scope? current = scope; current is { Transient: true }; current = current.Parent)
                foreach (var binding in current.Bindings.Values)
                {
                    state.Append(binding.Name).Append(':').Append(binding.Value.TypeName).Append('/');
                    state.Append(binding.Value.Element?.TypeName).Append(';');
                }

            return state.ToString();
        }

        private Value InferAssignment(ScriptNode node, Scope scope, int depth)
        {
            var target = node.Children[0];
            var value = Infer(node.Children[1], scope, depth + 1);
            if (node.Text != "=")
            {
                var previous = Infer(target, scope, depth + 1);
                value = node.Text == "+=" && (previous.TypeName == "string" || value.TypeName == "string")
                    ? Typed("string")
                    : Typed("number");
            }

            if (target.Kind is "index" or "member")
            {
                var container = Infer(target.Children[0], scope, depth + 1);
                if (target.Kind == "index" && container.TypeName == "array") container.Element = new Value();
                if (container.TypeName == "object")
                {
                    var key = target.Kind == "member" ? target.Text :
                        target.Children[1] is { Kind: "literal", Value: string field } ? field : null;
                    if (key is not null) container.Members[key] = value;
                    else container.Members.Clear();
                }
            }

            if (scope.Transient && target.Kind == "name" && scope.Find(target.Text, Offset(node)) is { } binding)
                binding.Value = binding.DeclaredType is { } type ? Typed(type) : value;
            return value;
        }

        private static void MergeInferenceScopes(Scope target, Scope? first, Scope? second)
        {
            var merged = new Dictionary<(Value? Left, Value? Right), Value>();
            for (Scope? current = target; current is { Transient: true }; current = current.Parent)
            {
                foreach (var pair in current.Bindings)
                {
                    var left = first?.Bindings.GetValueOrDefault(pair.Key)?.Value;
                    var right = second?.Bindings.GetValueOrDefault(pair.Key)?.Value;
                    if (left is null && right is null) continue;
                    if (!merged.TryGetValue((left, right), out var value))
                    {
                        value = left is null ? right! : right is null ? left : MergePatternValues(left, right);
                        merged[(left, right)] = value;
                    }

                    pair.Value.Value = value;
                }

                first = first?.Parent;
                second = second?.Parent;
            }
        }
    }
}
