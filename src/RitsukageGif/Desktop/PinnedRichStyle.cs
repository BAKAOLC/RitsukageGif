using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichStyle
    {
        internal string? FontFamily { get; init; }
        internal double FontSize { get; init; } = 14;
        internal FontWeight Weight { get; init; } = FontWeight.Normal;
        internal FontStyle Slant { get; init; } = FontStyle.Normal;
        internal Color? Foreground { get; init; }
        internal Color? Background { get; init; }
        internal bool Underline { get; init; }
        internal bool Strike { get; init; }
        internal bool PreserveWhitespace { get; init; }
        internal bool NoWrap { get; init; }
        internal BaselineAlignment Baseline { get; init; } = BaselineAlignment.Baseline;
        internal string? Link { get; init; }
    }
}
