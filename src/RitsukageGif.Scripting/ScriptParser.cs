namespace RitsukageGif.Scripting
{
    internal sealed partial class ScriptParser(IReadOnlyList<PresetToken> tokens, int start = 0, bool analysis = false)
    {
        private int _depth, _loops, _statementDepth;
        private int _tokenOffset;
        public int Position { get; private set; } = start;

        private PresetToken Current => _tokenOffset == 0
            ? tokens[Position]
            : tokens[Position] with
            {
                Text = tokens[Position].Text[_tokenOffset..], Column = tokens[Position].Column + _tokenOffset,
            };

        private bool Is(string value)
        {
            return !Current.Quoted && Current.Text == value;
        }

        private bool Take(string value)
        {
            if (!Is(value)) return false;
            Position++;
            _tokenOffset = 0;
            return true;
        }

        private PresetSyntaxException Error(string expected)
        {
            return new(Current.Line, Current.Column,
                UiText.Format("DslExpected", expected, Current.Text));
        }

        private void Need(string value)
        {
            if (value == ">" && Is(">="))
            {
                _tokenOffset = 1;
                return;
            }

            if (analysis && Is("<eof>") && value is ")" or "]" or ";") return;
            if (!Take(value)) throw Error(value);
        }

        private string Name()
        {
            if (Current.Quoted || Current.Text.Length == 0 ||
                !(char.IsLetter(Current.Text[0]) || Current.Text[0] == '_') ||
                Current.Text.Contains('.')) throw Error("identifier");
            return tokens[Position++].Text;
        }

        private string[] QualifiedNames()
        {
            if (Current.Quoted) throw Error("identifier");
            var names = Current.Text.Split('.');
            if (names.Any(name => name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') ||
                                  name.Any(c => !char.IsLetterOrDigit(c) && c != '_')))
                throw Error("identifier");
            Position++;
            return names;
        }

        private static ScriptNode ValidateTree(ScriptNode root)
        {
            var pending = new Stack<(ScriptNode Node, int Depth)>();
            pending.Push((root, 1));
            while (pending.TryPop(out var entry))
            {
                if (entry.Depth > 128)
                    throw new PresetSyntaxException(entry.Node.Line, entry.Node.Column, UiText.Get("DslTooComplex"));
                foreach (var child in entry.Node.Children) pending.Push((child, entry.Depth + 1));
                if (entry.Node.Binding is { } binding) pending.Push((binding, entry.Depth + 1));
                if (entry.Node.Parameters is { } parameters)
                    foreach (var parameter in parameters)
                        pending.Push((parameter, entry.Depth + 1));
            }

            return root;
        }

        private static ScriptNode Node(string kind, PresetToken token, IReadOnlyList<ScriptNode>? children = null,
            string? text = null, object? value = null)
        {
            return new(kind, text ?? token.Text, value, children ?? [], token.Line, token.Column);
        }

        public static ScriptNode Parse(string text)
        {
            return new ScriptParser(PresetLexer.Scan(text)).Document();
        }

        public static ScriptNode ParseForAnalysis(string text)
        {
            var tokens = PresetLexer.Scan(text, true);
            return new ScriptParser(tokens, analysis: true).Document();
        }

        public static ScriptNode ParseExpressionForAnalysis(string text)
        {
            return ValidateTree(new ScriptParser(PresetLexer.Scan(text, true), analysis: true).Expression());
        }

        private ScriptNode ReadStatement()
        {
            if (!analysis) return Statement();
            var start = Current;
            var depth = _depth;
            var loops = _loops;
            try
            {
                return Statement();
            }
            catch (PresetSyntaxException)
            {
                _depth = depth;
                _loops = loops;
                _tokenOffset = 0;
                while (!Is("<eof>") && !Is("}") && !Take(";")) Position++;
                return Node("error", start);
            }
        }

        private ScriptNode Document()
        {
            var token = Current;
            var nodes = new List<ScriptNode>();
            while (!Is("<eof>"))
            {
                var before = Position;
                nodes.Add(ReadStatement());
                if (Position == before) Position++;
            }

            return ValidateTree(Node("block", token, nodes));
        }

        public ScriptNode ParseBlock()
        {
            if (++_depth > 64) throw Error("nesting < 64");
            var token = Current;
            Need("{");
            var nodes = new List<ScriptNode>();
            while (!Take("}"))
            {
                if (Is("<eof>"))
                {
                    if (analysis) break;
                    throw Error("}");
                }

                nodes.Add(ReadStatement());
            }

            _depth--;
            return ValidateTree(Node("block", token, nodes));
        }

        private ScriptNode Declaration(bool semicolon = true)
        {
            var token = tokens[Position++];
            var binding = BindingPattern();
            var names = BindingNames(binding).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) throw Error("unique binding");
            var type = Take(":") ? TypeAnnotation() : null;
            binding = binding with { TypeName = type };
            Need("=");
            var value = Expression();
            if (semicolon) Need(";");
            return Node("declare", token, [value], binding.Kind == "binding-name" ? binding.Text : "",
                    token.Text == "const")
                with
                {
                    Binding = binding, TypeName = type,
                };
        }

        private ScriptNode Function(PresetToken token, string name = "")
        {
            Need("(");
            var parameters = Parameters();
            var returnType = Take(":") ? TypeAnnotation() : null;
            var outerLoops = _loops;
            _loops = 0;
            var body = ParseBlock();
            _loops = outerLoops;
            return FunctionNode(token, name, parameters, returnType, body);
        }

        private ScriptNode Statement()
        {
            if (++_statementDepth > 64) throw Error("nesting < 64");
            try
            {
                return StatementCore();
            }
            finally
            {
                _statementDepth--;
            }
        }

        private ScriptNode StatementCore()
        {
            var token = Current;
            if (Is("{")) return ParseBlock();
            if (Take(";")) return Node("empty", token);
            if (Take("export")) return Export(token);
            if (Take("import")) return Import(token);
            if (Is("let") || Is("const")) return Declaration();
            if (Take("fn") || Take("function"))
            {
                var name = Name();
                return Node("functionDeclaration", token, [Function(token, name)], name);
            }

            if (Take("if"))
            {
                Need("(");
                var condition = Expression();
                Need(")");
                var yes = Statement();
                var no = Take("else") ? Statement() : Node("empty", token);
                return Node("if", token, [condition, yes, no]);
            }

            if (Take("while"))
            {
                Need("(");
                var condition = Expression();
                Need(")");
                _loops++;
                var body = Statement();
                _loops--;
                return Node("while", token, [condition, body]);
            }

            if (Take("for"))
            {
                Need("(");
                ScriptNode init;
                var loopStart = Position;
                var loopOffset = _tokenOffset;
                if (Is("let") || Is("const"))
                {
                    var readOnly = tokens[Position++].Text == "const";
                    var binding = BindingPattern();
                    if (Take(":")) binding = binding with { TypeName = TypeAnnotation() };
                    if (Take("in") || Take("of"))
                    {
                        var names = BindingNames(binding).ToArray();
                        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                            throw Error("unique binding");
                        var sequence = Expression();
                        Need(")");
                        _loops++;
                        var body = Statement();
                        _loops--;
                        return Node("foreach", token, [sequence, body],
                                binding.Kind == "binding-name" ? binding.Text : "", readOnly)
                            with
                            {
                                Binding = binding,
                            };
                    }

                    Position = loopStart;
                    _tokenOffset = loopOffset;
                }

                if (Is("let") || Is("const")) init = Declaration(false);
                else init = Is(";") ? Node("empty", token) : Node("expression", token, [Expression()]);
                Need(";");
                var condition = Is(";") ? Node("literal", token, value: true) : Expression();
                Need(";");
                var update = Is(")") ? Node("literal", token) : Expression();
                Need(")");
                _loops++;
                var loopBody = Statement();
                _loops--;
                return Node("for", token, [init, condition, update, loopBody]);
            }

            if (Take("return"))
            {
                var value = Is(";") ? Node("literal", token) : Expression();
                Need(";");
                return Node("return", token, [value]);
            }

            if (Take("break") || Take("continue"))
            {
                if (_loops == 0) throw Error("loop");
                Need(";");
                return Node(token.Text, token);
            }

            if (Take("throw"))
            {
                var value = Expression();
                Need(";");
                return Node("throw", token, [value]);
            }

            if (Take("try"))
            {
                var body = ParseBlock();
                var handler = Node("empty", token);
                var final = Node("empty", token);
                var name = "";
                if (Take("catch"))
                {
                    Need("(");
                    name = Name();
                    Need(")");
                    handler = ParseBlock();
                }

                if (Take("finally")) final = ParseBlock();
                if (handler.Kind == "empty" && final.Kind == "empty") throw Error("catch/finally");
                return Node("try", token, [body, handler, final], name);
            }

            var expression = Expression();
            Need(";");
            return Node("expression", token, [expression]);
        }

        private static int Priority(string op)
        {
            return op switch
            {
                "=" or "+=" or "-=" or "*=" or "/=" or "%=" => 1, "?" => 2, "??" => 3, "||" or "or" => 4,
                "&&" or "and" => 5,
                "==" or "!=" => 6, "<" or ">" or "<=" or ">=" or "~" => 7, "+" or "-" => 8, "*" or "/" or "%" => 9,
                _ => 0,
            };
        }

        private ScriptNode Expression(int minimum = 1)
        {
            if (++_depth > 64) throw Error("nesting < 64");
            var token = Current;
            ScriptNode left;
            if (Current.Quoted)
            {
                Position++;
                left = Node("literal", token, value: token.Text);
            }
            else if (Take("true") || Take("false") || Take("null"))
            {
                left = Node("literal", token, value: token.Text == "null" ? null : token.Text == "true");
            }
            else if (Take("("))
            {
                var parameterStart = Position;
                var parameterOffset = _tokenOffset;
                var originalDepth = _depth;
                IReadOnlyList<ScriptNode>? parameters;
                string? returnType = null;
                try
                {
                    parameters = Parameters();
                    returnType = Take(":") ? TypeAnnotation() : null;
                    if (AtPatternGuardBoundary() || !Take("=>")) parameters = null;
                }
                catch (PresetSyntaxException)
                {
                    parameters = null;
                }

                if (parameters is not null)
                {
                    left = ArrowBody(token, parameters, returnType);
                }
                else
                {
                    Position = parameterStart;
                    _tokenOffset = parameterOffset;
                    _depth = originalDepth;
                    left = Node("group", token, [Expression()]);
                    Need(")");
                }
            }
            else if (Take("!") || Take("not") || Take("-") || Take("+"))
            {
                left = Node("unary", token, [Expression(10)]);
            }
            else if (Take("fn") || Take("function"))
            {
                left = Function(token);
            }
            else if (Take("["))
            {
                var items = new List<ScriptNode>();
                if (!Take("]"))
                {
                    do
                    {
                        if (Is("]")) break;
                        if (Is(","))
                        {
                            items.Add(Node("binding-hole", Current));
                            continue;
                        }

                        items.Add(SpreadExpression());
                    } while (Take(","));

                    Need("]");
                }

                left = Node("array", token, items);
            }
            else if (Take("{"))
            {
                var items = new List<ScriptNode>();
                if (!Take("}"))
                {
                    do
                    {
                        if (Is("}")) break;
                        var key = Current;
                        if (Take("..."))
                        {
                            items.Add(Node("spread", key, [Expression()]));
                            continue;
                        }

                        var name = Current.Quoted ? tokens[Position++].Text : Name();
                        ScriptNode value;
                        if (Take(":"))
                        {
                            value = Expression();
                        }
                        else
                        {
                            if (key.Quoted) throw Error(":");
                            value = Node("name", key, text: name);
                            if (Take("=")) value = Node("assign", key, [value, Expression()], "=");
                        }

                        items.Add(Node("pair", key, [value], name));
                    } while (Take(","));

                    Need("}");
                }

                left = Node("object", token, items);
            }
            else if (TryNumber(token.Text, out var number))
            {
                Position++;
                left = Node("literal", token, value: number);
            }
            else
            {
                var names = QualifiedNames();
                left = Node("name", token, text: names[0]);
                foreach (var name in names.Skip(1)) left = Node("member", token, [left], name);
                if (left.Kind == "name" && !AtPatternGuardBoundary() && Take("=>"))
                    left = ArrowBody(token, [Node("binding-name", token, text: left.Text)], null);
            }

            while (true)
            {
                var op = Current;
                var optional = Take("?.");
                if (Take("("))
                {
                    var arguments = new List<ScriptNode> { left };
                    if (!Take(")"))
                    {
                        do
                        {
                            if (Is(")")) break;
                            arguments.Add(SpreadExpression());
                        } while (Take(","));

                        Need(")");
                    }

                    left = Node(optional ? "optionalCall" : "call", op, arguments);
                    continue;
                }

                if (Take("["))
                {
                    var index = Is(":") ? Node("literal", op) : Expression();
                    if (Take(":"))
                    {
                        var end = Is(":") || Is("]") ? Node("literal", op) : Expression();
                        var step = Take(":") && !Is("]") ? Expression() : Node("literal", op);
                        Need("]");
                        left = Node(optional ? "optionalSlice" : "slice", op, [left, index, end, step]);
                        continue;
                    }

                    Need("]");
                    left = Node(optional ? "optionalIndex" : "index", op, [left, index]);
                    continue;
                }

                if (optional || Take("."))
                {
                    if (analysis && Is("<eof>"))
                    {
                        left = Node(optional ? "optionalMember" : "member", op, [left], "");
                        break;
                    }

                    foreach (var name in QualifiedNames())
                    {
                        left = Node(optional ? "optionalMember" : "member", op, [left], name);
                        optional = false;
                    }

                    continue;
                }

                if (Take("++") || Take("--"))
                {
                    while (left.Kind == "group") left = left.Children[0];
                    if (left.Kind is not ("name" or "member" or "index") || HasOptionalAccess(left))
                        throw Error("assignment target");
                    left = Node("postfix", op, [left]);
                    continue;
                }

                if (Take("switch"))
                {
                    left = SwitchExpression(left, op);
                    continue;
                }

                if (Is("is"))
                {
                    if (minimum > 7) break;
                    Position++;
                    var pattern = MatchPattern();
                    ValidatePatternBindings(pattern);
                    left = Node("is", op, [left, pattern]);
                    continue;
                }

                if (Is("?"))
                {
                    if (TryConditional(left, op, out var conditional, minimum > 2))
                    {
                        if (minimum > 2) break;
                        left = conditional;
                    }
                    else
                    {
                        Position++;
                        left = Node("propagate", op, [left]);
                    }

                    continue;
                }

                var priority = Current.Quoted ? 0 : Priority(op.Text);
                if (priority < minimum || priority == 0) break;
                Position++;
                var assignment = priority == 1;
                if (assignment)
                    while (left.Kind == "group")
                        left = left.Children[0];
                if (assignment && op.Text == "=" && left.Kind is "array" or "object") left = AssignmentPattern(left);
                if (assignment &&
                    (left.Kind is not ("name" or "member" or "index" or "binding-array" or "binding-object") ||
                     HasOptionalAccess(left)))
                    throw Error("assignment target");
                left = Node(assignment ? "assign" : "binary", op, [left, Expression(priority + (assignment ? 0 : 1))]);
            }

            _depth--;
            return left;
        }
    }
}
