using System.Globalization;

namespace RitsukageGif.Scripting
{
    internal sealed partial class ScriptParser
    {
        private static bool TryNumber(string text, out double number)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                try
                {
                    number = Convert.ToUInt64(text[2..], char.ToLowerInvariant(text[1]) == 'x' ? 16 : 2);
                    return true;
                }
                catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
                {
                    number = 0;
                    return false;
                }

            return double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
        }

        private string TypeAnnotation()
        {
            var complexity = 0;
            return ReadType(ref complexity);
        }

        private string ReadType(ref int complexity)
        {
            var parts = new List<string>();
            do
            {
                if (++complexity > 64) throw Error("type complexity < 64");
                string type;
                if (Take("("))
                {
                    type = "(" + ReadType(ref complexity) + ")";
                    Need(")");
                }
                else
                {
                    type = Name();
                    if (Take("<"))
                    {
                        var arguments = new List<string>();
                        do
                            arguments.Add(ReadType(ref complexity));
                        while (Take(","));

                        Need(">");
                        type += "<" + string.Join(",", arguments) + ">";
                    }
                }

                while (Is("[") || Is("?"))
                {
                    if (++complexity > 64) throw Error("type complexity < 64");
                    if (Take("?"))
                    {
                        type += "?";
                    }
                    else
                    {
                        Need("[");
                        Need("]");
                        type += "[]";
                    }
                }

                parts.Add(type);
            } while (Take("|"));

            return string.Join("|", parts);
        }

        private ScriptNode BindingPattern(bool allowDefault = false)
        {
            if (++_depth > 64) throw Error("nesting < 64");
            try
            {
                var token = Current;
                ScriptNode pattern;
                if (Take("["))
                {
                    var items = new List<ScriptNode>();
                    while (!Take("]"))
                    {
                        if (Take(","))
                        {
                            items.Add(Node("binding-hole", token));
                            continue;
                        }

                        if (Take("..."))
                        {
                            items.Add(Node("binding-rest", token, [BindingPattern()]));
                            Need("]");
                            break;
                        }

                        items.Add(BindingPattern(true));
                        if (Take("]")) break;
                        Need(",");
                    }

                    pattern = Node("binding-array", token, items);
                }
                else if (Take("{"))
                {
                    var items = new List<ScriptNode>();
                    while (!Take("}"))
                    {
                        var key = Current;
                        if (Take("..."))
                        {
                            items.Add(Node("binding-rest", key, [BindingPattern()]));
                            Need("}");
                            break;
                        }

                        var name = Current.Quoted ? tokens[Position++].Text : Name();
                        ScriptNode value;
                        if (Take(":"))
                        {
                            value = BindingPattern(true);
                        }
                        else
                        {
                            if (key.Quoted) throw Error(":");
                            value = Node("binding-name", key, text: name);
                            if (Take("=")) value = Node("binding-default", key, [value, Expression()]);
                        }

                        items.Add(Node("binding-property", key, [value], name));
                        if (Take("}")) break;
                        Need(",");
                    }

                    pattern = Node("binding-object", token, items);
                }
                else
                {
                    pattern = Node("binding-name", token, text: Name());
                }

                if (allowDefault && Take("=")) pattern = Node("binding-default", token, [pattern, Expression()]);
                return pattern;
            }
            finally
            {
                _depth--;
            }
        }

        private List<ScriptNode> Parameters()
        {
            var parameters = new List<ScriptNode>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (!Take(")"))
            {
                var token = Current;
                var rest = Take("...");
                var parameter = BindingPattern();
                var type = Take(":") ? TypeAnnotation() : null;
                parameter = parameter with { TypeName = type };
                if (!rest && Take("="))
                    parameter = Node("binding-default", token, [parameter, Expression()]) with { TypeName = type };
                foreach (var name in BindingNames(parameter))
                    if (!names.Add(name))
                        throw Error("unique parameter");
                parameters.Add(rest ? Node("binding-rest", token, [parameter]) with { TypeName = type } : parameter);
                if (Take(")")) break;
                if (rest) throw Error(")");
                Need(",");
            }

            return parameters;
        }

        private static IEnumerable<string> BindingNames(ScriptNode pattern)
        {
            if (pattern.Kind == "binding-name") yield return pattern.Text;
            else
                foreach (var child in pattern.Kind == "binding-default" ? pattern.Children.Take(1) : pattern.Children)
                foreach (var name in BindingNames(child))
                    yield return name;
        }

        private static ScriptNode FunctionNode(PresetToken token, string name, IReadOnlyList<ScriptNode> parameters,
            string? returnType, ScriptNode body)
        {
            return Node("function", token, [body], name,
                    parameters.Select(p => BindingNames(p).FirstOrDefault() ?? "").ToArray())
                with
                {
                    Parameters = parameters, TypeName = returnType,
                };
        }

        private ScriptNode ArrowBody(PresetToken token, IReadOnlyList<ScriptNode> parameters, string? returnType)
        {
            var outerLoops = _loops;
            _loops = 0;
            try
            {
                var body = Is("{") ? ParseBlock() : Node("block", token, [Node("return", token, [Expression()])]);
                return FunctionNode(token, "", parameters, returnType, body);
            }
            finally
            {
                _loops = outerLoops;
            }
        }

        private ScriptNode SpreadExpression()
        {
            var token = Current;
            return Take("...") ? Node("spread", token, [Expression()]) : Expression();
        }

        private ScriptNode AssignmentPattern(ScriptNode node)
        {
            while (node.Kind == "group") node = node.Children[0];
            if (HasOptionalAccess(node)) throw Error("assignment target");
            if (node.Kind is "array" or "object" && node.Children.Take(Math.Max(0, node.Children.Count - 1))
                    .Any(child => child.Kind == "spread"))
                throw Error("rest must be last");
            return node.Kind switch
            {
                "binding-hole" => node,
                "member" or "index" => node with { Kind = "binding-target", Children = [node] },
                "name" => node with { Kind = "binding-name" },
                "array" => node with
                {
                    Kind = "binding-array", Children = [.. node.Children.Select(AssignmentPattern)],
                },
                "object" => node with
                {
                    Kind = "binding-object", Children = [.. node.Children.Select(AssignmentPattern)],
                },
                "pair" => node with { Kind = "binding-property", Children = [AssignmentPattern(node.Children[0])] },
                "spread" => node with { Kind = "binding-rest", Children = [AssignmentPattern(node.Children[0])] },
                "assign" when node.Text == "=" => node with
                {
                    Kind = "binding-default", Children = [AssignmentPattern(node.Children[0]), node.Children[1]],
                },
                _ => throw Error("binding pattern"),
            };
        }

        private static bool HasOptionalAccess(ScriptNode node)
        {
            while (true)
            {
                if (node.Kind is "optionalMember" or "optionalIndex" or "optionalCall" or "optionalSlice") return true;
                if (node.Kind is not ("member" or "index" or "call" or "slice")) return false;
                node = node.Children[0];
            }
        }
    }
}
