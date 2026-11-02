using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed record ImagePreviewLayout(Rect Visible, PixelRect Sampling, PixelSize WorkSize)
    {
        public double ScaleX => (double)WorkSize.Width / Sampling.Width;
        public double ScaleY => (double)WorkSize.Height / Sampling.Height;
        public Matrix Transform => new(ScaleX, 0, 0, ScaleY, -Sampling.X * ScaleX, -Sampling.Y * ScaleY);

        public Rect VisibleInWork => new((Visible.X - Sampling.X) * ScaleX,
            (Visible.Y - Sampling.Y) * ScaleY, Visible.Width * ScaleX, Visible.Height * ScaleY);

        public PixelRect WorkBounds => new(0, 0, WorkSize.Width, WorkSize.Height);
    }
}
