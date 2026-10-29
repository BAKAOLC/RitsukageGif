namespace RitsukageGif.Scripting
{
    public static class PresetLanguage
    {
        public static readonly string[] Keywords =
        [
            "let", "const", "fn", "function", "if", "else", "for", "in", "while", "break", "continue", "return",
            "import", "export", "from", "default", "as", "try", "catch", "finally", "throw", "and", "or", "not", "true",
            "false", "null", "switch", "is", "var", "when",
        ];

        internal static readonly string[] DeclarationWords =
        [
            "preset", "script", "window", "process", "title", "source", "screen", "area", "client", "follow",
            "missing", "transparent", "color", "image", "record", "file", "ffmpeg", "channel", "delay",
            "loop", "speed", "position", "live", "holdLast", "staleAfter", "size",
            "copy", "save", "to", "layout", "background", "region", "place", "shape", "rect",
            "ellipse", "rounded", "polygon", "initial", "state", "enter", "exit", "on", "when", "after", "repeat",
            "wait", "await", "timeout", "goto", "start", "stop", "cancel", "select", "show", "hide", "finish",
            "emit", "log", "assert", "breakpoint", "move", "visible", "opacity", "off",
            "exists", "recording", "busy", "idle", "width", "height", "elapsed",
            "found", "lost", "closed", "moved", "resized", "stopped", "saved", "cancelled", "error",
        ];

        public static IReadOnlyList<ScriptSpan> Scan(string text)
        {
            var spans = new List<ScriptSpan>();
            int at = 0, line = 1, column = 1, depth = 0, scriptDepth = -1;
            var presetDocument = false;
            var firstWord = true;
            var pendingScript = false;

            while (at < text.Length)
            {
                if (char.IsWhiteSpace(text[at]))
                {
                    Advance();
                    continue;
                }

                var start = at;
                var row = line;
                var col = column;
                var kind = "operator";
                switch (text[at])
                {
                    case '/' when at + 1 < text.Length && text[at + 1] == '*':
                    {
                        kind = "comment";
                        Advance();
                        Advance();
                        while (at < text.Length)
                        {
                            if (text[at] == '*' && at + 1 < text.Length && text[at + 1] == '/')
                            {
                                Advance();
                                Advance();
                                break;
                            }

                            Advance();
                        }

                        break;
                    }
                    case '#':
                    case '/' when at + 1 < text.Length && text[at + 1] == '/':
                    {
                        kind = "comment";
                        while (at < text.Length && text[at] != '\n') Advance();
                        break;
                    }
                    case '"' or '\'':
                    {
                        kind = "string";
                        var quote = text[at];
                        Advance();
                        while (at < text.Length)
                        {
                            var c = text[at];
                            Advance();
                            if (c == '\\' && at < text.Length) Advance();
                            else if (c == quote) break;
                        }

                        break;
                    }
                    case var character when char.IsLetter(character) || character == '_':
                    {
                        while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) Advance();
                        var word = text[start..at];
                        if (firstWord)
                        {
                            presetDocument = word == "preset";
                            firstWord = false;
                        }

                        var declaration = presetDocument && scriptDepth < 0;
                        var next = at;
                        while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                        var previous = spans.LastOrDefault()?.Text;
                        var member = previous is "." or "?." ||
                                     (next < text.Length && text[next] == ':' && previous is "{" or ",");
                        kind = !member && (Keywords.Contains(word) || (declaration && DeclarationWords.Contains(word)))
                            ? "keyword"
                            : next < text.Length && text[next] == '('
                                ? "function"
                                : "identifier";
                        if (declaration && word == "script") pendingScript = true;
                        break;
                    }
                    case var character when char.IsAsciiDigit(character):
                    {
                        kind = "number";
                        while (at < text.Length &&
                               (char.IsAsciiLetterOrDigit(text[at]) || text[at] is '.' or '%' or '_' ||
                                (text[at] is '+' or '-' && at > start && text[at - 1] is 'e' or 'E')))
                            Advance();
                        break;
                    }
                    default:
                    {
                        switch (text[at])
                        {
                            case '{':
                            {
                                depth++;
                                if (pendingScript)
                                {
                                    scriptDepth = depth;
                                    pendingScript = false;
                                }

                                break;
                            }
                            case '}':
                                depth--;
                                if (depth < scriptDepth) scriptDepth = -1;
                                break;
                        }

                        var optionalOperator = text[at] == '?' && at + 1 < text.Length && text[at + 1] is '?' or '.';
                        Advance();
                        if (optionalOperator) Advance();
                        break;
                    }
                }

                spans.Add(new(start, at - start, kind, text[start..at], row, col));
            }

            return spans;

            void Advance()
            {
                if (text[at++] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }
        }

        public static IReadOnlyList<ScriptDiagnostic> Diagnose(string text, ScriptBindingRegistry? bindings = null,
            string? file = null)
        {
            try
            {
                if (Scan(text).FirstOrDefault(t => t.Kind != "comment")?.Text == "preset")
                    WindowPreset.Parse(text, bindings);
                else ScriptEngine.Compile(text);
                return [];
            }
            catch (PresetSyntaxException e)
            {
                return [new(e.Line, e.Column, e.RawMessage, file ?? e.SourceFile)];
            }
            catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException)
            {
                return [new(1, 1, e.Message, file)];
            }
        }

        public static ScriptSemanticModel Analyze(string text, ScriptBindingRegistry? bindings = null,
            string? file = null, ScriptSourceResolver? sources = null)
        {
            return new(text, bindings, file, sources);
        }

        // ReSharper disable once UnusedMember.Global
        public static IReadOnlyList<ScriptSymbol> Symbols(string text)
        {
            return Analyze(text).Symbols;
        }

        // ReSharper disable once UnusedMember.Global
        public static IReadOnlyList<ScriptCompletion> Complete(string text, int offset)
        {
            return Analyze(text).Complete(offset);
        }

        internal static bool IsScriptContext(IReadOnlyList<ScriptSpan> spans)
        {
            if (spans.FirstOrDefault(t => t.Kind != "comment")?.Text != "preset") return true;
            var depth = 0;
            var scriptDepth = -1;
            var pending = false;
            foreach (var span in spans)
            {
                if (span is { Kind: "keyword", Text: "script" }) pending = true;
                if (span.Kind != "operator") continue;
                switch (span.Text)
                {
                    case "{":
                    {
                        depth++;
                        if (pending)
                        {
                            scriptDepth = depth;
                            pending = false;
                        }

                        break;
                    }
                    case "}":
                        depth--;
                        if (depth < scriptDepth) scriptDepth = -1;
                        break;
                }
            }

            return scriptDepth >= 0;
        }

        public static ScriptSpan? WordAt(string text, int offset)
        {
            return WordAt(Scan(text), offset);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public static ScriptSpan? WordAt(IReadOnlyList<ScriptSpan> spans, int offset)
        {
            var index = -1;
            for (var i = 0; i < spans.Count; i++)
                if (spans[i].Offset <= offset && offset <= spans[i].Offset + spans[i].Length)
                    index = i;
            if (index < 0) return null;
            var word = spans[index];
            if (index >= 2 && spans[index - 1].Text == ".")
                return word with { Text = spans[index - 2].Text + "." + word.Text };
            return word;
        }

        // ReSharper disable once UnusedMember.Global
        public static string Hint(string word)
        {
            return Analyze(word).Hint(word.Length) ?? word;
        }

        public static IReadOnlyList<int> ExecutableLines(string text)
        {
            if (Scan(text).FirstOrDefault(t => t.Kind != "comment")?.Text == "preset")
                return [.. WindowPreset.Parse(text).SelectMany(ExecutableLines).Distinct().Order()];
            return [.. ScriptLines(ScriptEngine.Compile(text)).Distinct().Order()];
        }

        private static IEnumerable<int> ScriptLines(ScriptNode node)
        {
            if (node.Kind is "declare" or "functionDeclaration" or "expression" or "if" or "while" or "for" or "foreach"
                or "return" or "break" or "continue" or "throw" or "try" or "import") yield return node.Line;
            foreach (var child in node.Children)
            foreach (var line in ScriptLines(child))
                yield return line;
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public static IReadOnlyList<int> ExecutableLines(WindowPreset preset)
        {
            var scriptLines = preset.Program is null ? [] : ScriptLines(preset.Program);
            return
            [
                .. Walk(preset.States.SelectMany(s => s.Enter.Concat(s.Exit).Concat(s.Rules.SelectMany(r => r.Actions)))
                        .Concat(preset.Rules.SelectMany(r => r.Actions))
                        .Concat((preset.Functions ?? []).SelectMany(f => f.Body)))
                    .Where(a => a.Line > 0).Select(a => a.Line).Concat(scriptLines).Distinct().Order(),
            ];

            IEnumerable<PresetAction> Walk(IEnumerable<PresetAction> actions)
            {
                return actions.SelectMany(a =>
                    new[] { a }.Concat(Walk(a.Body ?? [])).Concat(Walk(a.Otherwise ?? [])));
            }
        }
    }
}
