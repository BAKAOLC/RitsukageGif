using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace RitsukageGif.Desktop
{
    internal sealed class ScriptColorizer : DocumentColorizingTransformer
    {
        public IReadOnlyList<ScriptSpan> Tokens { get; set; } = [];
        public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; set; } = [];
        public HashSet<int> Breakpoints { get; set; } = [];
        public int CurrentLine { get; set; }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (line.Length == 0) return;
            if (CurrentLine == line.LineNumber || Breakpoints.Contains(line.LineNumber))
                ChangeLinePart(line.Offset, line.EndOffset,
                    e => e.TextRunProperties.SetBackgroundBrush(CurrentLine == line.LineNumber
                        ? Brushes.LemonChiffon
                        : Brushes.MistyRose));
            var low = 0;
            var high = Tokens.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (Tokens[middle].Offset + Tokens[middle].Length <= line.Offset) low = middle + 1;
                else high = middle;
            }

            for (var index = low; index < Tokens.Count; index++)
            {
                var token = Tokens[index];
                if (token.Offset >= line.EndOffset) break;
                if (token.Offset + token.Length <= line.Offset) continue;
                var brush = token.Kind switch
                {
                    "keyword" => Brushes.RoyalBlue, "string" => Brushes.Brown, "comment" => Brushes.SeaGreen,
                    "number" => Brushes.DarkViolet, "function" => Brushes.SaddleBrown, _ => Brushes.DarkSlateGray
                };
                ChangeLinePart(Math.Max(line.Offset, token.Offset),
                    Math.Min(line.EndOffset, token.Offset + token.Length),
                    e => e.TextRunProperties.SetForegroundBrush(brush));
            }

            foreach (var error in Diagnostics.Where(d => d.Line == line.LineNumber))
            {
                var start = Math.Min(line.EndOffset - 1, line.Offset + Math.Max(0, error.Column - 1));
                var token = Tokens.FirstOrDefault(span => span.Offset <= start && span.Offset + span.Length > start);
                var end = Math.Min(line.EndOffset, token is null ? start + 1 : token.Offset + token.Length);
                ChangeLinePart(start, end, e => e.TextRunProperties.SetBackgroundBrush(Brushes.MistyRose));
            }
        }
    }
}
