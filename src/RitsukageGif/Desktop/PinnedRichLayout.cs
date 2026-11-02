using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichLayout : IDisposable
    {
        private readonly List<double> _bottoms = [];
        private readonly Dictionary<PinnedRichLine, TextLine> _cache = [];
        private readonly Queue<PinnedRichLine> _cacheOrder = [];
        private readonly Action<DrawingContext, PinnedRichImage, Rect> _drawImage;
        private readonly IBrush _foreground;
        private readonly Func<PinnedRichImage, Size> _imageSize;
        private readonly List<PinnedRichLine> _lines = [];
        private readonly string _plainText;
        private readonly CancellationToken _token;

        // ReSharper disable once ReplaceWithFieldKeyword
        private readonly double _viewportWidth;
        private readonly List<PinnedRichLine> _visualLines = [];
        private bool _relativeContext;
        private bool _relativeWidth;

        internal PinnedRichLayout(PinnedRichDocument document, double width, IBrush foreground,
            Func<PinnedRichImage, Size> imageSize, Action<DrawingContext, PinnedRichImage, Rect> drawImage,
            CancellationToken token = default)
        {
            _foreground = foreground;
            _imageSize = imageSize;
            _drawImage = drawImage;
            _token = token;
            _plainText = document.PlainText;
            _viewportWidth = width;
            _relativeContext = double.IsNaN(document.Style.Width.Value) || document.Style.Width.Percent;
            _relativeWidth = _relativeContext && (document.Style.Width.Percent || document.Style.Height.Percent);
            Root = new() { Bounds = new(0, 0, width, 0), Style = new() };
            var margin = document.Style.Margin;
            var bodyWidth = Math.Max(1, document.Style.Width.Resolve(width,
                width - margin.Left - margin.Right));
            var body = new PinnedRichBox
            {
                Bounds = new(margin.Left, margin.Top, bodyWidth, 0), Style = document.Style,
            };
            Root.Children.Add(body);
            var inset = document.Style.Padding + document.Style.Border;
            var height = LayoutBlocks(document.Blocks, body.Children, margin.Left + inset.Left,
                margin.Top + inset.Top, Math.Max(1, bodyWidth - inset.Left - inset.Right));
            body.Bounds = body.Bounds.WithHeight(Math.Max(height + inset.Top + inset.Bottom,
                document.Style.Height.Resolve(bodyWidth, 0)));
            Root.Bounds = new(0, 0, Math.Max(width, body.Bounds.Right + margin.Right),
                Math.Max(1, body.Bounds.Bottom + margin.Bottom));
            CollectLines(Root);
            Root.Bounds = Root.Bounds.WithWidth(Math.Max(width,
                _lines.Count > 0 ? _lines.Max(line => line.Bounds.Right) + inset.Right + margin.Right : width));
            UpdateVisualBounds(Root);
            _visualLines.AddRange(_lines.OrderBy(line => line.Bounds.Top));
            var bottom = 0d;
            foreach (var line in _visualLines)
            {
                bottom = Math.Max(bottom, line.Bounds.Bottom);
                _bottoms.Add(bottom);
            }
        }

        private PinnedRichBox Root { get; }
        internal Size Size => Root.Bounds.Size;

        internal double PreferredWidth => _relativeWidth
            ? _viewportWidth
            : _plainText.Length > 4096
                ? Size.Width
                : Math.Max(1, Math.Ceiling(PreferredRight(Root)));

        public void Dispose()
        {
            foreach (var line in _cache.Values) line.Dispose();
            _cache.Clear();
            _cacheOrder.Clear();
        }

        private double PreferredRight(PinnedRichBox box)
        {
            var inset = box.Style.Padding + box.Style.Border;
            var right = box.Bounds.X + inset.Left;
            if (!double.IsNaN(box.PreferredContentWidth))
            {
                right += box.PreferredContentWidth;
            }
            else
            {
                foreach (var line in box.Lines)
                {
                    var end = line.Start + line.Length;
                    if (line.Length > 0 && end < line.Paragraph.Start + line.Paragraph.Length &&
                        _plainText[end - 1] is not ('\r' or '\n' or '\u2028' or '\u2029'))
                        return box.Bounds.Right + box.Style.Margin.Right;
                    right = Math.Max(right, line.Bounds.X + Realize(line).WidthIncludingTrailingWhitespace +
                                            (line.Offset == 0 ? Math.Max(0, line.Paragraph.Style.Indent) : 0));
                }

                // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                foreach (var child in box.Children) right = Math.Max(right, PreferredRight(child));
            }

            right += inset.Right + box.Style.Margin.Right;
            return !double.IsNaN(box.Style.Width.Value) || box.Rule
                ? Math.Max(right, box.Bounds.Right + box.Style.Margin.Right)
                : right;
        }

        private IEnumerable<PinnedRichLine> VisibleLines(Rect viewport)
        {
            var low = 0;
            var high = _visualLines.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_bottoms[middle] < viewport.Top) low = middle + 1;
                else high = middle;
            }

            for (var index = low; index < _visualLines.Count; index++)
            {
                var line = _visualLines[index];
                if (line.Bounds.Top > viewport.Bottom) yield break;
                if (line.Bounds.Intersects(viewport)) yield return line;
            }
        }

        internal IEnumerable<string> ImageSources(Rect viewport)
        {
            foreach (var line in VisibleLines(viewport))
            foreach (var run in line.Paragraph.Runs)
            {
                if (run.Image is null || run.Start >= line.Start + line.Length ||
                    run.Start + run.Text.Length <= line.Start) continue;
                if (Realize(line).GetTextBounds(run.Start - line.Paragraph.Start, run.Text.Length)
                    .Any(bounds => bounds.Rectangle.Translate(line.Bounds.Position).Intersects(viewport)))
                    yield return run.Image.Source;
            }
        }

        private void CollectLines(PinnedRichBox box)
        {
            _lines.AddRange(box.Lines);
            foreach (var child in box.Children) CollectLines(child);
        }

        private static Rect UpdateVisualBounds(PinnedRichBox box)
        {
            var bounds = box.Bounds;
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var line in box.Lines) bounds = bounds.Union(line.Bounds);
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var child in box.Children) bounds = bounds.Union(UpdateVisualBounds(child));
            box.VisualBounds = bounds;
            return bounds;
        }

        private double LayoutBlocks(IReadOnlyList<PinnedRichBlock> blocks, List<PinnedRichBox> boxes,
            double x, double y, double width)
        {
            var top = y;
            foreach (var block in blocks)
            {
                _token.ThrowIfCancellationRequested();
                var box = LayoutBlock(block, x, y, width);
                boxes.Add(box);
                y = box.Bounds.Bottom + block.Style.Margin.Bottom;
            }

            return y - top;
        }

        private PinnedRichBox LayoutBlock(PinnedRichBlock block, double x, double y, double width)
        {
            var style = block.Style;
            var relativeContext = _relativeContext;
            _relativeContext &= double.IsNaN(style.Width.Value) || style.Width.Percent;
            _relativeWidth |= _relativeContext && (style.Width.Percent || style.Height.Percent);
            x += style.Margin.Left;
            y += style.Margin.Top;
            width = Math.Max(1, width - style.Margin.Left - style.Margin.Right);
            width = Math.Max(1, Math.Min(width, style.Width.Resolve(width, width)));
            var box = new PinnedRichBox
            {
                Bounds = new(x, y, width, 0), Style = style,
                Rule = block is PinnedRichRule,
            };
            var inset = style.Padding + style.Border;
            var innerX = x + inset.Left;
            var innerY = y + inset.Top;
            var innerWidth = Math.Max(1, width - inset.Left - inset.Right);
            var height = block switch
            {
                PinnedRichParagraph paragraph => LayoutParagraph(paragraph, box, innerX, innerY, innerWidth),
                PinnedRichContainer container => LayoutBlocks(container.Blocks, box.Children, innerX, innerY,
                    innerWidth),
                PinnedRichTable table => LayoutTable(table, box, innerX, innerY, innerWidth),
                PinnedRichRule => 2,
                _ => 0,
            };
            box.Bounds = new(x, y, width, Math.Max(height + inset.Top + inset.Bottom,
                style.Height.Resolve(width, 0)));
            _relativeContext = relativeContext;
            return box;
        }

        private double LayoutParagraph(PinnedRichParagraph paragraph, PinnedRichBox box,
            double x, double y, double width)
        {
            var source =
                new PinnedRichTextSource(paragraph, Typeface.Default, _foreground, width, _imageSize, _drawImage);
            var firstStyle = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Style : new();
            _relativeWidth |= _relativeContext && paragraph.Runs.Any(run =>
                run.Image is { } image && (image.Width.Percent || image.Height.Percent));
            var position = 0;
            var top = y;
            TextLineBreak? previous = null;
            TextLine? pending = null;
            try
            {
                do
                {
                    _token.ThrowIfCancellationRequested();
                    var properties = new GenericTextParagraphProperties(paragraph.Style.Direction,
                        paragraph.Style.Alignment, position == 0, false, source.Properties(firstStyle),
                        firstStyle.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap,
                        paragraph.Style.LineHeight, position == 0 ? paragraph.Style.Indent : 0, 0);
                    var line = TextFormatter.Current.FormatLine(source, position, width, properties, previous)
                               ?? throw new InvalidOperationException("HTML text formatting returned no line.");
                    pending?.Dispose();
                    pending = line;
                    var length = Math.Min(line.Length, Math.Max(0, paragraph.Length - position));
                    box.Lines.Add(new()
                    {
                        Paragraph = paragraph, Source = source, Properties = properties,
                        Bounds = new(x, y, Math.Max(width, line.WidthIncludingTrailingWhitespace),
                            Math.Max(1, line.Height)),
                        Offset = position, Length = length, FormatWidth = width,
                    });
                    y += Math.Max(1, line.Height);
                    if (line.Length <= 0) break;
                    position += line.Length;
                    previous = line.TextLineBreak;
                } while (position < paragraph.Length);
            }
            finally
            {
                pending?.Dispose();
            }

            return y - top;
        }

        private TextLine Realize(PinnedRichLine line)
        {
            if (_cache.TryGetValue(line, out var cached)) return cached;
            var result = TextFormatter.Current.FormatLine(
                             new PinnedRichLineSource(line.Source, line.Offset + line.Length),
                             line.Offset, line.FormatWidth, line.Properties)
                         ?? throw new InvalidOperationException("HTML text formatting returned no line.");
            _cache.Add(line, result);
            _cacheOrder.Enqueue(line);
            while (_cache.Count > 256)
            {
                var evicted = _cacheOrder.Dequeue();
                _cache.Remove(evicted, out var formatted);
                formatted?.Dispose();
            }

            return result;
        }
    }
}
