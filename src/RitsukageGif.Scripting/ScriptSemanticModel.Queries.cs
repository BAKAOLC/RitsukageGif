namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptSemanticModel
    {
        private Value ExpressionBefore(int end, int offset)
        {
            if (end >= 2 && _source.AsSpan(end - 2, 2) is "?.") end -= 2;
            var tokens = _tokens.Where(t => t.Offset < end).ToArray();
            if (tokens.Length == 0) return new();
            var depth = 0;
            var start = tokens.Length - 1;
            for (; start >= 0; start--)
            {
                var text = tokens[start].Text;
                if (depth == 0 && start < tokens.Length - 1 && text == "}" &&
                    tokens[start + 1].Kind is "identifier" or "function") break;
                if (text is ")" or "]" or "}")
                {
                    depth++;
                }
                else if (text is "(" or "[" or "{")
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (depth == 0 && text is ";" or "," or "=" or ":" or "+" or "-" or "*" or "/" or "return"
                             or "let" or "const" or "??" or "=>" or "when" or "&&" or "and" or ">" or "<" or "&" or "|"
                             or "?")
                {
                    break;
                }
            }

            start++;
            if (start >= tokens.Length) return new();
            try
            {
                var expression = ScriptParser.ParseExpressionForAnalysis(_source[tokens[start].Offset..end]);
                var line = Array.BinarySearch(_lineStarts, offset);
                if (line < 0) line = ~line - 1;

                return Infer(Locate(expression), _root.At(offset));

                ScriptNode Locate(ScriptNode node)
                {
                    return node with
                    {
                        Line = line + 1, Column = offset - _lineStarts[line] + 1,
                        Children = [.. node.Children.Select(Locate)],
                    };
                }
            }
            catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
            {
                return new();
            }
        }

        private CallSite? FindCall(int offset)
        {
            var stack = new Stack<(ScriptSpan Token, int Index)>();
            var tokens = _tokens.Where(t => t.Offset < offset).ToArray();
            for (var i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Kind != "operator") continue;
                switch (tokens[i].Text)
                {
                    case "(" or "[" or "{": stack.Push((tokens[i], i)); break;
                    case ")" or "]" or "}" when stack.Count > 0: stack.Pop(); break;
                }
            }

            foreach (var (token, index) in stack)
            {
                if (token.Text != "(") continue;
                var target = ExpressionBefore(token.Offset, offset);
                if (Callable(target) is null) continue;
                var argument = 0;
                var depth = 0;
                var argumentStart = index + 1;
                for (var i = index + 1; i < tokens.Length; i++)
                {
                    if (tokens[i].Kind != "operator") continue;
                    switch (tokens[i].Text)
                    {
                        case "(" or "[" or "{": depth++; break;
                        case ")" or "]" or "}": depth--; break;
                        case "," when depth == 0:
                            argument++;
                            argumentStart = i + 1;
                            break;
                    }
                }

                return new(target, argument, [.. tokens.Skip(argumentStart)]);
            }

            return null;
        }

        private static string ParameterLabel(ScriptParameter parameter)
        {
            return (parameter.Variadic ? "..." : "") + parameter.Name + (parameter.Optional ? "?" : "") + ": " +
                   parameter.TypeName;
        }

        private static string Label(ScriptCallableDescriptor callable)
        {
            return callable.Name + "(" + string.Join(", ", callable.Parameters.Select(ParameterLabel)) + "): " +
                   DisplayReturnType(callable.ReturnTypeName);
        }

        public ScriptSignature? Signature(int offset)
        {
            var call = FindCall(Math.Clamp(offset, 0, _source.Length));
            var descriptor = call is null ? null : Callable(call.Target);
            return descriptor is null
                ? null
                : new ScriptSignature(Label(descriptor),
                    [.. descriptor.Parameters.Select(ParameterLabel)],
                    Math.Min(call!.Argument, Math.Max(0, descriptor.Parameters.Count - 1)), descriptor.Description);
        }

        private IEnumerable<ScriptCompletion> EnumItems(string type, bool quoted, bool declaration)
        {
            return TypeAlternatives(type).Select(_bindings.GetEnum).OfType<ScriptEnumDescriptor>()
                .DistinctBy(enumeration => enumeration.Name)
                .SelectMany(enumeration => enumeration.Values.Keys.Select(name => new ScriptCompletion(name,
                    quoted || declaration
                        ? name
                        : char.IsLetter(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c == '_')
                            ? enumeration.Name + "." + name
                            : enumeration.Name + "[\"" + name + "\"]",
                    enumeration.Name + "." + name, "enumMember")));
        }

        private IEnumerable<ScriptCompletion>? ContextItems(int offset, int prefixStart, bool quoted)
        {
            var call = FindCall(offset);
            string? type = null;
            IReadOnlyList<ScriptSpan> tail = [];
            var declaration = false;
            if (call is not null && Callable(call.Target) is { Parameters.Count: > 0 } descriptor)
            {
                type = descriptor.Parameters[Math.Min(call.Argument, descriptor.Parameters.Count - 1)].TypeName;
                tail = call.Tail;
            }
            else if (!PresetLanguage.IsScriptContext([.. _tokens.Where(t => t.Offset < offset)]))
            {
                var stack = new Stack<int>();
                for (var i = 0; i < _tokens.Length && _tokens[i].Offset < offset; i++)
                    switch (_tokens[i].Text)
                    {
                        case "{": stack.Push(i); break;
                        case "}" when stack.Count > 0: stack.Pop(); break;
                    }

                foreach (var index in stack)
                {
                    if (index == 0 || _bindings.GetDeclarationSchema(_tokens[index - 1].Text) is not
                            { } schema) continue;
                    type = schema;
                    tail = [.. _tokens.Skip(index + 1).TakeWhile(t => t.Offset < offset)];
                    declaration = true;
                    break;
                }
            }

            if (type is null) return null;
            if (TypeAlternatives(type).Any(alternative => _bindings.GetEnum(alternative) is not null))
                return EnumItems(type, quoted, declaration);
            var members = _bindings.GetMembers(type);
            if (members.Count == 0) return null;
            var fields = tail.Where(t => t.Offset < prefixStart).ToArray();
            var lastSeparator = Array.FindLastIndex(fields, t => t.Text is "," or ";" or "{");
            var current = fields.Skip(lastSeparator + 1).ToArray();
            if (current.Length > 0)
            {
                var member = members.FirstOrDefault(m => m.Name == current[0].Text.Trim('"', '\''));
                if (member is not null) return EnumItems(member.TypeName, quoted, declaration);
            }

            return members.Select(m => new ScriptCompletion(m.Name, m.Name, m.Name + ": " + m.TypeName, "property"));
        }

        private bool IsTypeContext(int prefixStart)
        {
            var before = _tokens.Where(t => t.Offset < prefixStart).ToArray();
            if (before.Length == 0 || before[^1].Text is not (":" or "|")) return false;
            var boundary = Array.FindLastIndex(before, t => t.Text is ";" or "{" or "}");
            var header = before.Skip(boundary + 1).ToArray();
            return header.Any(t => t.Text is "fn" or "function") ||
                   (header.FirstOrDefault()?.Text is "let" or "const" && header.All(t => t.Text != "="));
        }

        public IReadOnlyList<ScriptCompletion> Complete(int offset)
        {
            offset = Math.Clamp(offset, 0, _source.Length);
            var active = _tokens.LastOrDefault(t => t.Offset < offset && t.Offset + t.Length >= offset);
            if (active?.Kind == "comment") return [];
            var quoted = active?.Kind == "string";
            var start = offset;
            while (start > 0 && (char.IsLetterOrDigit(_source[start - 1]) || _source[start - 1] == '_' ||
                                 (quoted && _source[start - 1] == '-'))) start--;
            var prefix = _source[start..offset];
            IEnumerable<ScriptCompletion> items;
            var previous = _tokens.LastOrDefault(t => t.Offset < start);
            switch (quoted, previous)
            {
                case (false, { Text: "." or "?." } member):
                    var receiver = ExpressionBefore(member.Offset, offset);
                    items = Members(receiver).Select(pair => new ScriptCompletion(pair.Key, pair.Key,
                        Describe(pair.Value, pair.Key),
                        _bindings.GetEnum(receiver.TypeName) is not null ? "enumMember" :
                        Callable(pair.Value) is null ? "property" : "method"));
                    break;
                case (false, _) when IsTypeContext(start):
                    items = _bindings.GetTypeNames().Select(type => new ScriptCompletion(type, type, type, "class"));
                    break;
                case (_, _) when ContextItems(offset, start, quoted) is { } context:
                    items = context;
                    break;
                case (true, _):
                    return [];
                default:
                    var visible = new Dictionary<string, Binding>();
                    for (var scope = _root.At(offset); scope is not null; scope = scope.Parent)
                        foreach (var pair in scope.Bindings)
                            if (pair.Value.Offset <= offset)
                                visible.TryAdd(pair.Key, pair.Value);
                    items = visible.Values.Select(b => new ScriptCompletion(b.Name, b.Name,
                        Describe(b.Value, b.Name),
                        b.Kind));
                    items = items.Concat(_bindings.GetPatterns().Select(pattern => new ScriptCompletion(pattern.Name,
                        pattern.Name,
                        pattern.Name + "(" + string.Join(", ", pattern.PayloadTypes.Select(DisplayReturnType)) + "): " +
                        pattern.InputType, "function")));
                    items = items.Concat(PresetLanguage.Keywords.Concat(
                        PresetLanguage.IsScriptContext([.. _tokens.Where(t => t.Offset < offset)])
                            ? []
                            : PresetLanguage.DeclarationWords).Select(k => new ScriptCompletion(k, k, k)));
                    break;
            }

            return
            [
                .. items.Where(c => c.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .DistinctBy(c => c.Label).OrderBy(c => c.Label),
            ];
        }

        private string Describe(Value value, string name)
        {
            var callable = Callable(value, name);
            var label = callable is null ? name + ": " + value.TypeName : Label(callable);
            var description = value.Description ?? callable?.Description;
            return string.IsNullOrWhiteSpace(description) ? label : label + "\n" + description;
        }

        public string? Hint(int offset)
        {
            var token = _tokens.LastOrDefault(t => t.Offset <= offset && offset <= t.Offset + t.Length);
            if (token is null || token.Kind is "string" or "comment") return null;
            var value = ExpressionBefore(token.Offset + token.Length, offset);
            return value is { TypeName: "any", Description: null } ? token.Text : Describe(value, token.Text);
        }

        public ScriptSymbol? Definition(int offset)
        {
            var token = _tokens.LastOrDefault(t => t.Offset <= offset && offset <= t.Offset + t.Length);
            if (token is null) return null;
            var previous = _tokens.LastOrDefault(t => t.Offset < token.Offset);
            var symbol = previous?.Text is "." or "?."
                ? ExpressionBefore(token.Offset + token.Length, offset).Declaration
                : _root.At(offset).Find(token.Text, offset)?.Declaration;
            return symbol?.Line > 0 ? symbol : null;
        }

        private sealed record CallSite(Value Target, int Argument, IReadOnlyList<ScriptSpan> Tail);
    }
}
