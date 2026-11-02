using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRichBox
    {
        internal required Rect Bounds { get; set; }
        internal required PinnedRichBlockStyle Style { get; init; }
        internal Rect VisualBounds { get; set; }
        internal double PreferredContentWidth { get; set; } = double.NaN;
        internal List<PinnedRichLine> Lines { get; } = [];
        internal List<PinnedRichBox> Children { get; } = [];
        internal bool Rule { get; init; }

        internal void Move(Vector delta)
        {
            Bounds = Bounds.Translate(delta);
            foreach (var line in Lines) line.Bounds = line.Bounds.Translate(delta);
            foreach (var child in Children) child.Move(delta);
        }
    }
}
