using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly record struct ImageTextSelectionSpan(int Start, int Length, int Line, Rect Bounds);
}
