using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace RitsukageGif.Desktop
{
    internal sealed class ScriptColorizer : DocumentColorizingTransformer
    {
        private static readonly IBrush DarkKeyword = new SolidColorBrush(Color.Parse("#569CD6"));
        private static readonly IBrush DarkString = new SolidColorBrush(Color.Parse("#CE9178"));
        private static readonly IBrush DarkComment = new SolidColorBrush(Color.Parse("#6A9955"));
        private static readonly IBrush DarkNumber = new SolidColorBrush(Color.Parse("#B5CEA8"));
        private static readonly IBrush DarkFunction = new SolidColorBrush(Color.Parse("#DCDCAA"));
        private static readonly IBrush DarkCurrentLine = new SolidColorBrush(Color.Parse("#3B3824"));
        private static readonly IBrush DarkError = new SolidColorBrush(Color.Parse("#502A2A"));

        public IReadOnlyList<ScriptSpan> Tokens { get; set; } = [];
        public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; set; } = [];
        public HashSet<int> Breakpoints { get; set; } = [];
        public int CurrentLine { get; set; }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (line.Length == 0) return;
            var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            if (CurrentLine == line.LineNumber || Breakpoints.Contains(line.LineNumber))
                ChangeLinePart(line.Offset, line.EndOffset,
                    e => e.TextRunProperties.SetBackgroundBrush(CurrentLine == line.LineNumber
                        ? dark ? DarkCurrentLine : Brushes.LemonChiffon
                        : dark
                            ? DarkError
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
                    "keyword" => dark ? DarkKeyword : Brushes.RoyalBlue,
                    "string" => dark ? DarkString : Brushes.Brown,
                    "comment" => dark ? DarkComment : Brushes.SeaGreen,
                    "number" => dark ? DarkNumber : Brushes.DarkViolet,
                    "function" => dark ? DarkFunction : Brushes.SaddleBrown, _ => AppTheme.Ink,
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
                ChangeLinePart(start, end,
                    e => e.TextRunProperties.SetBackgroundBrush(dark ? DarkError : Brushes.MistyRose));
            }
        }
    }
}
