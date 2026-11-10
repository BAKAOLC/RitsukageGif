using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly record struct RecognitionTextRange(Rect Bounds, int Start, int End);
}
