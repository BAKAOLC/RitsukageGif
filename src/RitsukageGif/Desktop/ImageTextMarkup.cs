using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageTextMarkup
    {
        private readonly Dictionary<(int Start, ImageTextMarkKind Kind), ImageTextMark> _marks = [];

        internal bool IsEmpty => _marks.Count == 0;

        internal bool ContainsAny(ImageTextSelection selection)
        {
            foreach (var span in selection.SelectedSpans)
                if (_marks.ContainsKey((span.Start, ImageTextMarkKind.Highlight)) ||
                    _marks.ContainsKey((span.Start, ImageTextMarkKind.Underline)) ||
                    _marks.ContainsKey((span.Start, ImageTextMarkKind.WavyUnderline)) ||
                    _marks.ContainsKey((span.Start, ImageTextMarkKind.Strikethrough)))
                    return true;
            return false;
        }

        private bool Contains(ImageTextSelection selection, ImageTextMarkKind kind)
        {
            if (!selection.HasSelection) return false;
            foreach (var span in selection.SelectedSpans)
                if (!_marks.ContainsKey((span.Start, kind)))
                    return false;
            return true;
        }

        internal bool Toggle(ImageTextSelection selection, ImageTextMarkKind kind, Color color)
        {
            var remove = Contains(selection, kind);
            Set(selection, kind, remove ? null : color);
            return !remove;
        }

        internal void Set(ImageTextSelection selection, ImageTextMarkKind kind, Color? color)
        {
            foreach (var (start, length, line, bounds) in selection.SelectedSpans)
            {
                if (color is not { } value)
                {
                    _marks.Remove((start, kind));
                    continue;
                }

                if (kind != ImageTextMarkKind.Highlight)
                {
                    _marks.Remove((start, ImageTextMarkKind.Underline));
                    _marks.Remove((start, ImageTextMarkKind.WavyUnderline));
                    _marks.Remove((start, ImageTextMarkKind.Strikethrough));
                }

                _marks[(start, kind)] = new()
                {
                    Start = start, Length = length, Line = line, Kind = kind, Color = value,
                    X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
                };
            }
        }

        internal ImageTextMark[] Snapshot()
        {
            return [.. _marks.Values.OrderBy(mark => mark.Line).ThenBy(mark => mark.Kind).ThenBy(mark => mark.Start)];
        }

        internal void Restore(ImageTextMark[] marks, Rect? canvas = null)
        {
            foreach (var mark in marks)
                if (!mark.IsValid || (canvas is { } region && !region.Contains(mark.Bounds)))
                    throw new InvalidDataException("Invalid pinned image text markup.");
            _marks.Clear();
            foreach (var mark in marks) _marks[(mark.Start, mark.Kind)] = mark;
        }

        internal IEnumerable<(ImageTextMarkKind Kind, Color Color, Rect Bounds)> Runs()
        {
            ImageTextMark? previous = null;
            var bounds = default(Rect);
            foreach (var mark in Snapshot())
            {
                var current = mark.Bounds;
                if (previous is not null && previous.Line == mark.Line && previous.Kind == mark.Kind &&
                    previous.Color == mark.Color && previous.Start + previous.Length == mark.Start &&
                    Math.Abs(bounds.Top - current.Top) < .01 && Math.Abs(bounds.Height - current.Height) < .01)
                {
                    bounds = bounds.Union(current);
                }
                else
                {
                    if (previous is not null) yield return (previous.Kind, previous.Color, bounds);
                    bounds = current;
                }

                previous = mark;
            }

            if (previous is not null) yield return (previous.Kind, previous.Color, bounds);
        }
    }
}
