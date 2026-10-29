namespace RitsukageGif.Scripting
{
    internal sealed partial class ScriptParser
    {
        private int _patternGuardStart = -1;

        private bool AtPatternGuardBoundary()
        {
            if (_patternGuardStart < 0) return false;
            var depth = 0;
            for (var i = _patternGuardStart; i < Position; i++)
            {
                if (tokens[i].Quoted) continue;
                switch (tokens[i].Text)
                {
                    case "(" or "[" or "{": depth++; break;
                    case ")" or "]" or "}": depth--; break;
                }
            }

            return depth == 0;
        }

        private ScriptNode PatternGuard()
        {
            var outerStart = _patternGuardStart;
            _patternGuardStart = Position;
            try
            {
                return Expression();
            }
            finally
            {
                _patternGuardStart = outerStart;
            }
        }

        private ScriptNode SwitchExpression(ScriptNode subject, PresetToken token)
        {
            Need("{");
            var arms = new List<ScriptNode> { subject };
            while (!Take("}"))
            {
                var arm = Current;
                var pattern = MatchPattern();
                ValidatePatternBindings(pattern);
                var guard = Take("when") ? PatternGuard() : Node("literal", arm, value: true);
                Need("=>");
                arms.Add(Node("switchArm", arm, [pattern, guard, Expression()]));
                if (Take("}")) break;
                Need(",");
            }

            if (arms.Count == 1) throw Error("switch arm");
            return Node("switch", token, arms);
        }

        private ScriptNode MatchPattern(int minimum = 1)
        {
            if (++_depth > 64) throw Error("nesting < 64");
            try
            {
                var token = Current;
                ScriptNode pattern;
                if (Take("not"))
                {
                    pattern = Node("pattern-not", token, [MatchPattern(3)]);
                }
                else if (Is("(") && TryGroupedPatternType(out var groupedType))
                {
                    pattern = Node("pattern-type", token,
                        PatternBindingAhead() ? [Node("pattern-binding", Current, text: Name())] : [], groupedType);
                }
                else if (Take("("))
                {
                    pattern = MatchPattern();
                    Need(")");
                }
                else if (Take("_"))
                {
                    pattern = Node("pattern-wildcard", token);
                }
                else if (Take("var"))
                {
                    pattern = Node("pattern-binding", token, text: Name());
                }
                else if (Is("<") || Is("<=") || Is(">") || Is(">="))
                {
                    Position++;
                    pattern = Node("pattern-relational", token, [PatternLiteral()]);
                }
                else if (Take("{"))
                {
                    pattern = ObjectPattern(token);
                }
                else if (Take("["))
                {
                    pattern = ArrayPattern(token);
                }
                else if (Current.Quoted || Is("true") || Is("false") || Is("null") || Is("-") || Is("+") ||
                         TryNumber(Current.Text, out _))
                {
                    pattern = PatternLiteral();
                }
                else
                {
                    var start = Position;
                    var name = string.Join(".", QualifiedNames());
                    if (Take("("))
                    {
                        var arguments = new List<ScriptNode>();
                        if (!Take(")"))
                        {
                            do
                                arguments.Add(MatchPattern());
                            while (Take(",") && !Is(")"));

                            Need(")");
                        }

                        pattern = Node("pattern-constructor", token, arguments, name);
                    }
                    else
                    {
                        if (Is("<") || Is("["))
                        {
                            Position = start;
                            name = TypeAnnotation();
                        }

                        pattern = Node("pattern-type", token,
                            PatternBindingAhead() ? [Node("pattern-binding", Current, text: Name())] : [], name);
                    }
                }

                while (Current is { Quoted: false, Text: "and" or "or" })
                {
                    var priority = Is("and") ? 2 : 1;
                    if (priority < minimum) break;
                    var op = tokens[Position++];
                    pattern = Node("pattern-" + op.Text, op, [pattern, MatchPattern(priority + 1)]);
                }

                return pattern;
            }
            finally
            {
                _depth--;
            }
        }

        private bool TryGroupedPatternType(out string type)
        {
            var position = Position;
            var offset = _tokenOffset;
            try
            {
                type = TypeAnnotation();
                if (type.EndsWith(']') || type.EndsWith('?') || type.Contains('|') || PatternBindingAhead())
                    return true;
            }
            catch (PresetSyntaxException)
            {
            }

            Position = position;
            _tokenOffset = offset;
            type = "";
            return false;
        }

        private bool PatternBindingAhead()
        {
            return Current is { Quoted: false, Text.Length: > 0 } &&
                   (char.IsLetter(Current.Text[0]) || Current.Text[0] == '_') &&
                   Current.Text is not ("when" or "and" or "or" or "not" or "is"
                       or "switch");
        }

        private ScriptNode PatternLiteral()
        {
            var token = Current;
            if (Current.Quoted)
            {
                Position++;
                return Node("pattern-literal", token, value: token.Text);
            }

            if (Take("true") || Take("false") || Take("null"))
                return Node("pattern-literal", token, value: token.Text == "null" ? null : token.Text == "true");
            var negative = Take("-");
            if (!negative) Take("+");
            if (!TryNumber(Current.Text, out var value)) throw Error("literal pattern");
            Position++;
            return Node("pattern-literal", token, value: negative ? -value : value);
        }

        private ScriptNode ObjectPattern(PresetToken token)
        {
            var properties = new List<ScriptNode>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (!Take("}"))
            {
                var property = Current;
                var name = Current.Quoted ? tokens[Position++].Text : Name();
                if (!names.Add(name)) throw Error("unique property pattern");
                Need(":");
                properties.Add(Node("pattern-property", property, [MatchPattern()], name));
                if (Take("}")) break;
                Need(",");
            }

            return Node("pattern-object", token, properties);
        }

        private ScriptNode ArrayPattern(PresetToken token)
        {
            var items = new List<ScriptNode>();
            var hasRest = false;
            while (!Take("]"))
            {
                var item = Current;
                var rest = Take("...");
                if (!rest && Take("."))
                {
                    Need(".");
                    rest = true;
                }

                if (rest)
                {
                    if (hasRest) throw Error("one rest pattern");
                    hasRest = true;
                    var variable = Take("var");
                    var bindingToken = Current;
                    var binding = Take("_") || (!variable && (Is(",") || Is("]")))
                        ? Node("pattern-wildcard", bindingToken)
                        : Node("pattern-binding", bindingToken, text: Name());
                    items.Add(Node("pattern-rest", item, [binding]));
                }
                else
                {
                    items.Add(MatchPattern());
                }

                if (Take("]")) break;
                Need(",");
            }

            return Node("pattern-array", token, items);
        }

        private static void ValidatePatternBindings(ScriptNode pattern)
        {
            CollectPatternBindings(ValidateTree(pattern));
        }

        private static HashSet<string> CollectPatternBindings(ScriptNode pattern)
        {
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            switch (pattern.Kind)
            {
                case "pattern-binding":
                    bindings.Add(pattern.Text);
                    break;
                case "pattern-or":
                {
                    bindings = CollectPatternBindings(pattern.Children[0]);
                    if (!bindings.SetEquals(CollectPatternBindings(pattern.Children[1])))
                        throw new PresetSyntaxException(pattern.Line, pattern.Column,
                            UiText.Get("ScriptPatternBindings"));
                    break;
                }
                default:
                {
                    foreach (var name in pattern.Children.SelectMany(CollectPatternBindings))
                        if (!bindings.Add(name))
                            throw new PresetSyntaxException(pattern.Line, pattern.Column,
                                UiText.Get("ScriptPatternBindings"));
                    if (pattern.Kind == "pattern-not" && bindings.Count > 0)
                        throw new PresetSyntaxException(pattern.Line, pattern.Column,
                            UiText.Get("ScriptPatternBindings"));
                    break;
                }
            }

            return bindings;
        }
    }
}
