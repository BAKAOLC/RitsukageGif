using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedTextStyleRange(
        int Start,
        int End,
        Typeface Typeface,
        IBrush? Foreground,
        IBrush? Background,
        double Size,
        TextDecorationCollection? Decorations);
}
