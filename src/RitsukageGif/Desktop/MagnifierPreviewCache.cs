using Avalonia;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class MagnifierPreviewCache : IDisposable
    {
        public required Rect Source { get; init; }
        public required bool ShowAnnotations { get; init; }
        public required RenderTargetBitmap Bitmap { get; init; }
        public required IReadOnlyList<(PixelRect Region, byte[] Pixels)> Frames { get; init; }
        public required IReadOnlyList<MagnifierSourceState> Layers { get; init; }

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }
}
