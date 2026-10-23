namespace RitsukageGif.Scripting
{
    internal sealed partial class ScriptParser
    {
        private readonly List<int> _conditionalBranches = [];
        private int _conditionalAttempts;

        private bool HasConditionalBranch()
        {
            var pending = 0;
            foreach (var branch in _conditionalBranches)
            {
                var depth = 0;
                for (var i = branch + 1; i < Position; i++)
                {
                    if (tokens[i].Quoted) continue;
                    if (tokens[i].Text is "(" or "[" or "{") depth++;
                    else if (tokens[i].Text is ")" or "]" or "}") depth--;
                }

                if (depth == 0) pending++;
            }

            var nesting = 0;
            var colons = 0;
            for (var i = Position + 1; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Quoted) continue;
                var text = token.Text;
                if (text is "(" or "[" or "{")
                {
                    nesting++;
                    continue;
                }

                if (text is ")" or "]" or "}")
                {
                    if (nesting == 0) break;
                    nesting--;
                    continue;
                }

                if (nesting != 0) continue;
                if (text is ";" or "," or "<eof>") break;
                if (text == ":") colons++;
            }

            return colons > pending;
        }

        private bool TryConditional(ScriptNode left, PresetToken token, out ScriptNode conditional, bool probe)
        {
            conditional = left;
            if (!HasConditionalBranch()) return false;
            if (++_conditionalAttempts > Math.Min(65536, (long)tokens.Count * 16))
                throw new PresetSyntaxException(token.Line, token.Column, UiText.Get("DslTooComplex"));
            var position = Position;
            var tokenOffset = _tokenOffset;
            var depth = _depth;
            var loops = _loops;
            var statementDepth = _statementDepth;
            var branches = _conditionalBranches.Count;
            Position++;
            _conditionalBranches.Add(position);
            try
            {
                var yes = Expression();
                Need(":");
                _conditionalBranches.RemoveAt(_conditionalBranches.Count - 1);
                conditional = Node("conditional", token, [left, yes, Expression(2)]);
                if (probe)
                {
                    Position = position;
                    _tokenOffset = tokenOffset;
                    _depth = depth;
                    _loops = loops;
                    _statementDepth = statementDepth;
                }

                return true;
            }
            catch (PresetSyntaxException)
            {
                Position = position;
                _tokenOffset = tokenOffset;
                _depth = depth;
                _loops = loops;
                _statementDepth = statementDepth;
                return false;
            }
            finally
            {
                if (_conditionalBranches.Count > branches)
                    _conditionalBranches.RemoveRange(branches, _conditionalBranches.Count - branches);
            }
        }
    }
}
