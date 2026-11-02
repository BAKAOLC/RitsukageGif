using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageTextSelection(ImageTextSelectionDocument document)
    {
        private readonly (Rect Bounds, int Start, int Count)[] _lines = GroupLines(document.Spans);
        private int _anchor = -1;
        private int _end = -1;
        private int _pending = -1;
        private bool IsDragging { get; set; }
        internal bool HasSelection => _anchor >= 0;

        internal ReadOnlySpan<ImageTextSelectionSpan> SelectedSpans => _anchor < 0
            ? []
            : document.Spans.Slice(Math.Min(_anchor, _end), Math.Abs(_anchor - _end) + 1);

        internal string SelectedText
        {
            get
            {
                var selected = SelectedSpans;
                return selected.IsEmpty
                    ? ""
                    : document.Text.Substring(selected[0].Start,
                        selected[^1].Start + selected[^1].Length - selected[0].Start);
            }
        }

        internal int Hit(Point point, bool nearest = false)
        {
            var spans = document.Spans;
            var best = -1;
            var distance = double.PositiveInfinity;
            foreach (var line in _lines)
            {
                if ((!nearest && !line.Bounds.Contains(point)) ||
                    (nearest && Distance(line.Bounds, point) > distance)) continue;
                for (var index = line.Start; index < line.Start + line.Count; index++)
                {
                    var rectangle = spans[index].Bounds;
                    if (rectangle.Contains(point)) return index;
                    if (!nearest) continue;
                    var current = Distance(rectangle, point);
                    if (current >= distance) continue;
                    distance = current;
                    best = index;
                }
            }

            return best;
        }

        private static double Distance(Rect rectangle, Point point)
        {
            var dx = Math.Max(Math.Max(rectangle.X - point.X, point.X - rectangle.Right), 0);
            var dy = Math.Max(Math.Max(rectangle.Y - point.Y, point.Y - rectangle.Bottom), 0);
            return dx * dx + dy * dy;
        }

        private static (Rect Bounds, int Start, int Count)[] GroupLines(ReadOnlySpan<ImageTextSelectionSpan> spans)
        {
            var lines = new List<(Rect, int, int)>();
            for (var index = 0; index < spans.Length;)
            {
                var start = index;
                var bounds = spans[index].Bounds;
                while (++index < spans.Length && spans[index].Line == spans[start].Line)
                    bounds = bounds.Union(spans[index].Bounds);
                lines.Add((bounds, start, index - start));
            }

            return [.. lines];
        }

        internal bool Begin(Point point)
        {
            _pending = Hit(point);
            IsDragging = false;
            if (_pending >= 0) return true;
            Clear();
            return false;
        }

        internal bool Extend(Point point)
        {
            if (_pending < 0) return false;
            _anchor = _pending;
            _end = Hit(point, true);
            IsDragging = true;
            return true;
        }

        internal void Finish()
        {
            if (_pending < 0) return;
            if (!IsDragging) Clear();
            _pending = -1;
            IsDragging = false;
        }

        private void Clear()
        {
            _pending = _anchor = _end = -1;
            IsDragging = false;
        }
    }
}
