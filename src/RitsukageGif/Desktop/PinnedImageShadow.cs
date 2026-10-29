using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedImageShadow(Window owner) : IDisposable
    {
        private readonly PinnedImageShadowStrip[] _strips =
            [new(), new(), new(), new()];

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var strip in _strips) strip.Dispose();
        }

        public void Refresh(uint color)
        {
            if (_disposed) return;
            var width = (int)Math.Ceiling(owner.ClientSize.Width * owner.RenderScaling);
            var height = (int)Math.Ceiling(owner.ClientSize.Height * owner.RenderScaling);
            if (width <= 0 || height <= 0) return;
            var margin = Math.Max(1, (int)Math.Ceiling(12 * owner.RenderScaling));
            var outerWidth = checked(width + margin * 2);
            var origin = owner.Position;
            var left = (int)Math.Max(int.MinValue, (long)origin.X - margin);
            var top = (int)Math.Max(int.MinValue, (long)origin.Y - margin);
            Span<PixelRect> regions =
            [
                new(left, top, outerWidth, margin),
                new(left, checked(origin.Y + height), outerWidth, margin),
                new(left, origin.Y, margin, height),
                new(checked(origin.X + width), origin.Y, margin, height),
            ];
            for (var index = 0; index < _strips.Length; index++)
                _strips[index].Refresh(owner, regions[index], width, height, margin, index, color);
        }
    }
}
