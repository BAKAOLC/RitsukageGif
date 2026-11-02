using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichBlockStyle
    {
        internal Thickness Margin { get; init; }
        internal Thickness Padding { get; init; }
        internal Thickness Border { get; init; }
        internal Color BorderColor { get; init; } = Colors.Gray;
        internal Color? Background { get; init; }
        internal TextAlignment Alignment { get; init; } = TextAlignment.Left;
        internal FlowDirection Direction { get; init; } = FlowDirection.LeftToRight;
        internal PinnedRichLength Width { get; init; } = PinnedRichLength.Auto;
        internal PinnedRichLength Height { get; init; } = PinnedRichLength.Auto;
        internal double LineHeight { get; init; } = double.NaN;
        internal double Indent { get; init; }
    }
}
