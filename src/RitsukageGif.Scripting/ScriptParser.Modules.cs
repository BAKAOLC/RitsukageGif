namespace RitsukageGif.Scripting
{
    internal sealed partial class ScriptParser
    {
        private ScriptNode Import(PresetToken token)
        {
            if (Current.Quoted)
            {
                var path = tokens[Position++].Text;
                if (Take(";")) return Node("importSideEffect", token, value: path);
                Need("as");
                var alias = Name();
                Need(";");
                return Node("import", token, text: alias, value: path);
            }

            var imports = new List<ScriptNode>();
            string? defaultName = null;
            if (!Is("{") && !Is("*"))
            {
                defaultName = Name();
                imports.Add(Node("importBinding", token, text: defaultName, value: "default"));
                if (!Is("from")) Need(",");
            }

            if (Take("*"))
            {
                Need("as");
                imports.Add(Node("importBinding", token, text: Name(), value: "*"));
            }
            else if (Take("{"))
            {
                while (!Take("}"))
                {
                    var member = Current;
                    var exported = Name();
                    var local = Take("as") ? Name() : exported;
                    imports.Add(Node("importBinding", member, text: local, value: exported));
                    if (Take("}")) break;
                    Need(",");
                }
            }
            else if (defaultName is null)
            {
                throw Error("import binding");
            }

            if (imports.Select(n => n.Text).Distinct(StringComparer.Ordinal).Count() != imports.Count)
                throw Error("unique import binding");
            Need("from");
            if (!Current.Quoted) throw Error("path");
            var file = tokens[Position++].Text;
            Need(";");
            return Node("importNamed", token, imports, value: file);
        }

        private ScriptNode Export(PresetToken token)
        {
            if (_statementDepth != 1) throw Error("module export");
            if (Take("default"))
            {
                if ((Is("fn") || Is("function")) && Position + 1 < tokens.Count && tokens[Position + 1].Text != "(")
                {
                    var keyword = tokens[Position++];
                    var name = Name();
                    var function = Function(keyword, name);
                    Take(";");
                    return Node("exportDefault", token, [function], name);
                }

                var expression = Expression();
                if (expression.Kind == "function") Take(";");
                else Need(";");
                return Node("exportDefault", token, [expression], "");
            }

            if (Take("{"))
            {
                var bindings = new List<ScriptNode>();
                while (!Take("}"))
                {
                    var member = Current;
                    var local = Name();
                    var exported = Take("as") ? Name() : local;
                    bindings.Add(Node("exportBinding", member, text: exported, value: local));
                    if (Take("}")) break;
                    Need(",");
                }

                Need(";");
                return Node("exportBindings", token, bindings);
            }

            if (Is("let") || Is("const")) return Node("exportDeclaration", token, [Declaration()]);
            if (Take("fn") || Take("function"))
            {
                var name = Name();
                var declaration = Node("functionDeclaration", token, [Function(token, name)], name);
                return Node("exportDeclaration", token, [declaration]);
            }

            throw Error("export declaration");
        }
    }
}
