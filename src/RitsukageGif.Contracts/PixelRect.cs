namespace RitsukageGif.Contracts
{
    public readonly record struct PixelRect(int X, int Y, int Width, int Height)
    {
        public int Right => checked(X + Width);
        public int Bottom => checked(Y + Height);
        public bool IsEmpty => Width <= 0 || Height <= 0;

        public bool Contains(PixelPoint p)
        {
            return p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;
        }

        public static PixelRect Between(PixelPoint a, PixelPoint b)
        {
            return new PixelRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        public PixelRect Intersect(PixelRect other)
        {
            var x = Math.Max(X, other.X);
            var y = Math.Max(Y, other.Y);
            return new PixelRect(x, y, Math.Max(0, Math.Min(Right, other.Right) - x),
                Math.Max(0, Math.Min(Bottom, other.Bottom) - y));
        }

        public (double X, double Y, double Width, double Height) ToLocal(PixelRect monitor, double scale)
        {
            if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            return ((X - monitor.X) / scale, (Y - monitor.Y) / scale, Width / scale, Height / scale);
        }
    }
}
