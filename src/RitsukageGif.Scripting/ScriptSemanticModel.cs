namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptSemanticModel
    {
        private sealed class Value(string typeName = "any")
        {
            public string TypeName { get; set; } = typeName;
            public Dictionary<string, Value> Members { get; } = new();
            public ScriptCallableDescriptor? Callable { get; init; }
            public string? Description { get; init; }
            public ScriptNode? Function { get; init; }
            public Scope? Closure { get; set; }
            public Value? Element { get; set; }
            public Value? Result { get; init; }
            public Value? Receiver { get; init; }
            public ScriptSymbol? Declaration { get; set; }
        }

        private sealed class Binding(string name, string kind, int offset, ScriptSymbol declaration, Value value)
        {
            public string Name { get; } = name;
            public string Kind { get; } = kind;
            public int Offset { get; } = offset;
            public ScriptSymbol Declaration { get; } = declaration;
            public Value Value { get; set; } = value;
            public string? DeclaredType { get; set; }
        }

        private sealed class Scope(int start, int end, Scope? parent)
        {
            public int Start { get; } = start;
            public int End { get; } = end;
            public Scope? Parent { get; } = parent;
            public bool Transient { get; init; } = parent?.Transient ?? false;
            public bool FunctionScope { get; init; }
            public Dictionary<string, Binding> Bindings { get; } = new();
            public List<Scope> Children { get; } = [];

            public Binding? Find(string name, int offset) =>
                Bindings.TryGetValue(name, out var local) && local.Offset <= offset
                    ? local
                    : Parent?.Find(name, offset);

            public Scope At(int offset) =>
                Children.LastOrDefault(s => s.Start <= offset && offset <= s.End)?.At(offset) ?? this;
        }

        private readonly string _source;
        private readonly IReadOnlyList<ScriptSpan> _tokens;
        private readonly ScriptBindingRegistry _bindings;
        private readonly Scope _root;
        private readonly Dictionary<int, int> _blockEnds = new();
        private readonly Dictionary<ScriptNode, Value> _functions = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ScriptNode> _builtFunctions = new(ReferenceEqualityComparer.Instance);
        private readonly List<ScriptSymbol> _symbols = [];
        private readonly List<ScriptDiagnostic> _diagnostics = [];
        private readonly int[] _lineStarts;
        public IReadOnlyList<ScriptSymbol> Symbols => _symbols;
        public IReadOnlyList<ScriptDiagnostic> Diagnostics => _diagnostics;

        public ScriptSemanticModel(string source, ScriptBindingRegistry? bindings = null, string? file = null,
            ScriptSourceResolver? sources = null) : this(source, bindings, file, sources ?? new ScriptSourceResolver(),
            [], new ModuleContext())
        {
        }

        private ScriptSemanticModel(string source, ScriptBindingRegistry? bindings, string? file,
            ScriptSourceResolver sources, IReadOnlyList<string> ancestry, ModuleContext moduleContext)
        {
            _file = file is null ? null : Path.GetFullPath(file);
            _sources = sources;
            _ancestry = ancestry;
            _moduleContext = moduleContext;
            _source = source;
            _tokens = PresetLanguage.Scan(source).Where(t => t.Kind != "comment").ToArray();
            _bindings = bindings ?? CaptureScriptLibrary.CreateBindings();
            _root = new Scope(0, source.Length, null);
            _lineStarts = new[] { 0 }
                .Concat(source.Select((c, i) => (c, i)).Where(p => p.c == '\n').Select(p => p.i + 1)).ToArray();
            var blocks = new Stack<int>();
            foreach (var token in _tokens)
            {
                if (token.Kind != "operator") continue;
                if (token.Text == "{") blocks.Push(token.Offset);
                else if (token.Text == "}" && blocks.TryPop(out var start)) _blockEnds[start] = token.Offset + 1;
            }

            foreach (var start in blocks) _blockEnds[start] = source.Length;
            foreach (var member in _bindings.GetGlobals())
                _root.Bindings[member.Name] = new Binding(member.Name, member.Kind, -1,
                    new ScriptSymbol(member.Name, member.Kind, 0, 0), FromMember(member));
            var code = source;
            if (_tokens.FirstOrDefault()?.Text == "preset")
            {
                var presetScopes = new List<Scope>();
                var masked = source.Select(c => c is '\r' or '\n' ? c : ' ').ToArray();
                for (var i = 0; i + 1 < _tokens.Count; i++)
                {
                    var token = _tokens[i];
                    if (token.Kind == "keyword" && token.Text == "preset" && i + 2 < _tokens.Count &&
                        _tokens[i + 2].Text == "{")
                    {
                        var opening = _tokens[i + 2].Offset;
                        var presetScope = new Scope(opening, _blockEnds.GetValueOrDefault(opening, source.Length),
                            _root);
                        presetScopes.Add(presetScope);
                        _root.Children.Add(presetScope);
                    }

                    if (token.Kind == "keyword" && token.Text is "preset" or "state" or "region")
                    {
                        var name = _tokens[i + 1];
                        _symbols.Add(new ScriptSymbol(name.Text.Trim('"', '\''), token.Text, name.Line, name.Column));
                    }

                    if (token.Kind != "keyword" || token.Text != "script" || _tokens[i + 1].Text != "{") continue;
                    var begin = _tokens[i + 1].Offset;
                    var end = _blockEnds.GetValueOrDefault(begin, source.Length);
                    source.CopyTo(begin, masked, begin, end - begin);
                }

                code = new string(masked);
                try
                {
                    var presets = WindowPreset.Parse(source, _bindings);
                    for (var index = 0; index < presets.Count && index < presetScopes.Count; index++)
                    {
                        var preset = presets[index];
                        foreach (var pair in preset.Constants)
                            AddRootValue(pair.Key, new Value("number"), presetScopes[index]);
                        foreach (var pair in preset.InitialValues ?? new Dictionary<string, object>())
                            AddRootValue(pair.Key, Literal(pair.Value), presetScopes[index]);
                    }
                }
                catch (FormatException)
                {
                }
            }

            try
            {
                var program = ScriptParser.ParseForAnalysis(code);
                BuildBlock(program, _root, true);
                CheckNode(program, null);
                CollectExports(program);
            }
            catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
            {
            }
        }

        private void AddRootValue(string name, Value value, Scope scope)
        {
            var token = _tokens.FirstOrDefault(t => t.Offset >= scope.Start && t.Offset < scope.End && t.Text == name);
            scope.Bindings[name] = new Binding(name, "variable", scope.Start,
                new ScriptSymbol(name, "let", token?.Line ?? 1, token?.Column ?? 1), value);
        }

        private int Offset(ScriptNode node) => Offset(node.Line, node.Column);

        private int Offset(int line, int column) =>
            Math.Clamp(_lineStarts[Math.Clamp(line - 1, 0, _lineStarts.Length - 1)] + column - 1, 0, _source.Length);

        private ScriptSymbol Declaration(ScriptNode node, string name, string kind)
        {
            var offset = Offset(node);
            var token = _tokens.FirstOrDefault(t => t.Offset >= offset && t.Text == name);
            return new ScriptSymbol(name, kind, token?.Line ?? node.Line, token?.Column ?? node.Column, _file);
        }

        private Value FromMember(ScriptMemberDescriptor member, string? elementType = null, Value? receiver = null)
        {
            var callable = member.Callable;
            if (callable is not null && elementType is not null)
                callable = callable with
                {
                    Parameters = callable.Parameters
                        .Select(p => p.ReceiverElement ? p with { TypeName = elementType } : p).ToArray()
                };
            return new Value(member.TypeName)
                { Callable = callable, Description = member.Description, Receiver = receiver };
        }

        private static Value Literal(object? value) => new(value switch
        {
            string => "string", bool => "boolean", null => "null", _ => "number"
        });

        private IReadOnlyDictionary<string, Value> Members(Value value)
        {
            var alternatives = TypeAlternatives(value.TypeName).Where(t => t != "null").ToArray();
            Dictionary<string, Value>? members = null;
            foreach (var type in alternatives)
            {
                var current = _bindings.GetMembers(type.EndsWith("[]", StringComparison.Ordinal) ? "array" : type)
                    .ToDictionary(m => m.Name, m => FromMember(m,
                        type.EndsWith("[]", StringComparison.Ordinal) ? value.Element?.TypeName : null, value));
                if (members is null) members = current;
                else
                    foreach (var key in members.Keys.ToArray())
                    {
                        if (current.TryGetValue(key, out var member))
                            members[key] = MergePatternValues(members[key], member);
                        else members.Remove(key);
                    }
            }

            members ??= new Dictionary<string, Value>();
            foreach (var pair in value.Members) members[pair.Key] = pair.Value;
            return members;
        }

        private void BuildBlock(ScriptNode block, Scope parent, bool root = false)
        {
            var start = Offset(block);
            var scope = root ? parent : new Scope(start, _blockEnds.GetValueOrDefault(start, parent.End), parent);
            if (!root) parent.Children.Add(scope);
            var declarations = block.Children.Select(n => n.Kind == "exportDeclaration"
                    ? n.Children[0]
                    : n.Kind == "exportDefault" && n.Text.Length > 0
                        ? new ScriptNode("functionDeclaration", n.Text, null, n.Children, n.Line, n.Column)
                        : n)
                .Where(n => n.Kind == "functionDeclaration").ToArray();
            foreach (var node in declarations)
            {
                var value = Function(node.Children[0], scope, true);
                var declaration = Declaration(node, node.Text, "fn");
                scope.Bindings[node.Text] = new Binding(node.Text, "function", scope.Start, declaration, value);
                _symbols.Add(declaration);
            }

            foreach (var node in block.Children) Build(node, scope);
            foreach (var node in declarations)
                FinishFunction(_functions[node.Children[0]]);
        }

        private void Build(ScriptNode node, Scope scope)
        {
            var children = node.Children;
            switch (node.Kind)
            {
                case "block":
                    BuildBlock(node, scope.At(Offset(node)));
                    return;
                case "declare":
                    var value = Infer(children[0], scope);
                    if (node.Binding is { } pattern)
                        BindPattern(pattern, value, scope, node.Value is true ? "const" : "let");
                    else AddBinding(node, node.Text, value, scope, node.Value is true ? "const" : "let", node.TypeName);
                    return;
                case "functionDeclaration": return;
                case "exportDeclaration":
                    Build(children[0], scope);
                    return;
                case "exportDefault":
                    Infer(children[0], scope);
                    return;
                case "exportBindings": return;
                case "for":
                case "foreach":
                    var loop = new Scope(Offset(node), BlockEnd(node, scope.End), scope);
                    scope.Children.Add(loop);
                    if (node.Kind == "for")
                    {
                        Build(children[0], loop);
                        Infer(children[1], loop);
                        BuildPositiveBranch(children[1], children[3], loop);
                        Infer(children[2], loop);
                    }
                    else
                    {
                        var item = Infer(children[0], scope).Element ?? new Value();
                        if (node.Binding is { } binding) BindPattern(binding, item, loop, "let");
                        else AddBinding(node, node.Text, item, loop, "let", null);
                        Build(children[1], loop);
                    }

                    return;
                case "try":
                    Build(children[0], scope);
                    if (children[1].Kind != "empty")
                    {
                        var handler = new Scope(Offset(children[1]), BlockEnd(children[1], scope.End), scope);
                        scope.Children.Add(handler);
                        handler.Bindings[node.Text] = new Binding(node.Text, "variable", handler.Start,
                            Declaration(node, node.Text, "let"), new Value());
                        BuildBlock(children[1], handler, true);
                    }

                    Build(children[2], scope);
                    return;
                case "import":
                case "importNamed":
                case "importSideEffect":
                    Import(node, scope);
                    return;
                case "if":
                    Infer(children[0], scope);
                    BuildPositiveBranch(children[0], children[1], scope);
                    Build(children[2], scope);
                    return;
                case "while":
                    Infer(children[0], scope);
                    BuildPositiveBranch(children[0], children[1], scope);
                    return;
                case "expression":
                    var expression = children[0];
                    if (expression.Kind == "assign")
                    {
                        var target = expression.Children[0];
                        var assigned = InferAssignment(expression, scope, 0);
                        if (target.Kind == "name" && scope.Find(target.Text, Offset(node)) is { } binding)
                            binding.Value = binding.DeclaredType is { } type ? Typed(type) : assigned;
                        else if (target.Kind == "member")
                            Infer(target.Children[0], scope).Members[target.Text] = assigned;
                    }
                    else Infer(expression, scope);

                    return;
                case "return":
                    if (children.Count > 0) Infer(children[0], scope);
                    return;
            }
        }

        private int BlockEnd(ScriptNode node, int fallback)
        {
            if (node.Kind == "block") return _blockEnds.GetValueOrDefault(Offset(node), fallback);
            foreach (var child in node.Children.Reverse())
            {
                var end = BlockEnd(child, -1);
                if (end >= 0) return end;
            }

            return fallback;
        }

        private Value Function(ScriptNode node, Scope parent, bool defer = false)
        {
            if (_functions.TryGetValue(node, out var existing)) return existing;
            var parameters = (string[])node.Value!;
            var function = new Value("function") { Function = node };
            _functions[node] = function;
            var body = node.Children[0];
            var scope = new Scope(Offset(body), _blockEnds.GetValueOrDefault(Offset(body), parent.End), parent)
                { FunctionScope = true };
            function.Closure = scope;
            parent.Children.Add(scope);
            if (node.Parameters is { } patterns)
                foreach (var pattern in patterns)
                    BindPattern(pattern, new Value(), scope, "parameter");
            else
                foreach (var name in parameters)
                    scope.Bindings[name] = new Binding(name, "variable", scope.Start,
                        Declaration(node, name, "parameter"), new Value());
            if (!defer) FinishFunction(function);
            return function;
        }

        private void FinishFunction(Value value)
        {
            if (value.Function is not { } node || value.Closure is not { } scope || !_builtFunctions.Add(node)) return;
            var saved = new Dictionary<Binding, Value>(ReferenceEqualityComparer.Instance);
            var copies = new Dictionary<Value, Value>(ReferenceEqualityComparer.Instance);
            var writes = InferenceWrites(node);
            var references = InferenceReferences(node);
            for (var parent = scope.Parent; parent is not null; parent = parent.Parent)
                foreach (var binding in parent.Bindings.Values)
                {
                    if (!writes.Contains(binding.Name) && (!references.Contains(binding.Name) ||
                                                           binding.Value.Element is null &&
                                                           binding.Value.Members.Count == 0)) continue;
                    saved[binding] = binding.Value;
                    binding.Value = CopyInferenceValue(binding.Value, copies);
                }

            try
            {
                BuildBlock(node.Children[0], scope, true);
            }
            finally
            {
                foreach (var pair in saved) pair.Key.Value = pair.Value;
            }
        }

        private Value Infer(ScriptNode node, Scope scope, int depth = 0)
        {
            if (depth > 24) return new Value();
            var children = node.Children;
            switch (node.Kind)
            {
                case "literal": return Literal(node.Value);
                case "group": return Infer(children[0], scope, depth + 1);
                case "propagate":
                    var propagated = Infer(children[0], scope, depth + 1);
                    return ScriptBindingRegistry.TryGetGenericType(propagated.TypeName, out var family,
                               out var arguments)
                           && _bindings.GetGenericTypeBinding(family)?.OutcomeValueParameter is { } valueParameter &&
                           valueParameter < arguments.Length
                        ? Typed(arguments[valueParameter])
                        : new Value();
                case "name": return scope.Find(node.Text, Offset(node))?.Value ?? new Value();
                case "member":
                case "optionalMember":
                    return Optional(
                        Members(Infer(children[0], scope, depth + 1)).GetValueOrDefault(node.Text) ?? new Value(),
                        node.Kind == "optionalMember");
                case "index":
                case "optionalIndex":
                    var indexed = Infer(children[0], scope, depth + 1);
                    return Optional(children[1] is { Kind: "literal", Value: string key }
                        ? Members(indexed).GetValueOrDefault(key) ?? new Value()
                        : indexed.Element ?? new Value(), node.Kind == "optionalIndex");
                case "object":
                    var obj = new Value("object");
                    foreach (var field in children)
                    {
                        if (field.Kind == "spread")
                        {
                            foreach (var pair in Members(Infer(field.Children[0], scope, depth + 1)))
                                obj.Members[pair.Key] = pair.Value;
                            continue;
                        }

                        var fieldValue = Infer(field.Children[0], scope, depth + 1);
                        fieldValue.Declaration = Declaration(field, field.Text, "property");
                        obj.Members[field.Text] = fieldValue;
                    }

                    return obj;
                case "array":
                    Value? element = null;
                    foreach (var item in children)
                    {
                        var itemValue = item.Kind == "spread"
                            ? Infer(item.Children[0], scope, depth + 1).Element ?? new Value()
                            : Infer(item, scope, depth + 1);
                        element = element is null ? itemValue : MergePatternValues(element, itemValue);
                    }

                    return new Value("array") { Element = element ?? Typed("never") };
                case "slice": return Infer(children[0], scope, depth + 1);
                case "optionalSlice": return Optional(Infer(children[0], scope, depth + 1), true);
                case "spread": return Infer(children[0], scope, depth + 1);
                case "function": return scope.Transient ? InferenceFunction(node, scope) : Function(node, scope);
                case "call":
                case "optionalCall":
                    var callable = Infer(children[0], scope, depth + 1);
                    if (callable.Result is { } result) return Optional(result, node.Kind == "optionalCall");
                    if (callable.Callable is { } native)
                        return Optional(InferNativeCall(native, callable.Receiver, children, scope, depth),
                            node.Kind == "optionalCall");
                    if (callable.Function is { } function && callable.Closure is { } closure)
                    {
                        if (!closure.Transient) FinishFunction(callable);
                        return Optional(InferFunctionCall(function, closure, children, scope, depth),
                            node.Kind == "optionalCall");
                    }

                    return new Value();
                case "switch": return InferSwitch(node, scope, depth);
                case "is":
                    Infer(children[0], scope, depth + 1);
                    return new Value("boolean");
                case "conditional":
                    Infer(children[0], scope, depth + 1);
                    var positive = PositiveScope(children[0], children[1], scope, Offset(children[2]) - 1);
                    return MergePatternValues(Infer(children[1], positive, depth + 1),
                        Infer(children[2], scope, depth + 1));
                case "unary": return node.Text is "!" or "not" ? new Value("boolean") : new Value("number");
                case "binary":
                    if (node.Text is "&&" or "and")
                    {
                        Infer(children[0], scope, depth + 1);
                        Infer(children[1], PositiveScope(children[0], children[1], scope), depth + 1);
                        return new Value("boolean");
                    }

                    if (node.Text == "??")
                    {
                        var left = Infer(children[0], scope, depth + 1);
                        var right = Infer(children[1], scope, depth + 1);
                        if (left.TypeName == "null") return right;
                        if (left.TypeName == "any") return left;
                        var alternatives = TypeAlternatives(left.TypeName).ToArray();
                        if (!alternatives.Contains("null", StringComparer.Ordinal)) return left;
                        var remaining = alternatives.Where(t => t != "null").ToArray();
                        return remaining.Length == 0
                            ? right
                            : MergePatternValues(Typed(string.Join("|", remaining)), right);
                    }

                    if (node.Text is "==" or "!=" or "<" or ">" or "<=" or ">=" or "&&" or "||" or "and" or "or" or "~")
                        return new Value("boolean");
                    return node.Text == "+" && (Infer(children[0], scope, depth + 1).TypeName == "string" ||
                                                Infer(children[1], scope, depth + 1).TypeName == "string")
                        ? new Value("string")
                        : new Value("number");
                case "assign": return InferAssignment(node, scope, depth);
                case "postfix": return new Value("number");
                default: return new Value();
            }
        }

        private static bool AlwaysReturns(ScriptNode node) => node.Kind switch
        {
            "return" or "throw" => true,
            "block" => node.Children.Any(AlwaysReturns),
            "if" => node.Children.Count > 2 && AlwaysReturns(node.Children[1]) && AlwaysReturns(node.Children[2]),
            "try" => AlwaysReturns(node.Children[2]) || AlwaysReturns(node.Children[0]) &&
                (node.Children[1].Kind == "empty" || AlwaysReturns(node.Children[1])),
            _ => false
        };
    }
}
