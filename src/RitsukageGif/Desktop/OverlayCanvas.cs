using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas : Control, IDisposable
    {
        private readonly WriteableBitmap _bitmap;
        private readonly PixelRect _monitor;
        private readonly SelectionEdgeDetector _edges;
        private readonly int _radius, _threshold;

        public OverlayCanvas(PixelRect monitor, byte[] pixels, int radius = 12, int threshold = 150)
        {
            _monitor = monitor;
            _edges = new SelectionEdgeDetector(monitor.Width, monitor.Height, pixels);
            _radius = Math.Clamp(radius, 0, 50);
            _threshold = Math.Clamp(threshold, 1, 765);
            _bitmap = new WriteableBitmap(new PixelSize(monitor.Width, monitor.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using var buffer = _bitmap.Lock();
            for (var y = 0; y < monitor.Height; y++)
                Marshal.Copy(pixels, y * monitor.Width * 4,
                    buffer.Address + y * buffer.RowBytes, monitor.Width * 4);
        }

        public PixelRect Selected { get; set; }
        public PixelPoint Mouse { get; set; }
        public int Mode { get; set; }
        public double Scale { get; set; } = 1;

        public void Dispose()
        {
            _bitmap.Dispose();
        }

        public PixelPoint Snap(PixelPoint point, int mode)
        {
            if (mode == 0 || _radius == 0) return point;
            var x = point.X - _monitor.X;
            var y = point.Y - _monitor.Y;
            var sx = x;
            var sy = y;
            // Local edge sampling is bounded to the monitor; no giant virtual-screen Sobel bitmap.
            if ((mode & 2) != 0) sx = _edges.Find(x, y, true, _radius, _threshold, Scale);
            if ((mode & 1) != 0) sy = _edges.Find(y, x, false, _radius, _threshold, Scale);
            return new PixelPoint(sx + _monitor.X, sy + _monitor.Y);
        }

        public override void Render(DrawingContext context)
        {
            var all = new Rect(Bounds.Size);
            context.DrawImage(_bitmap, new Rect(0, 0, _monitor.Width, _monitor.Height), all);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), all);
            var intersection = Selected.Intersect(_monitor);
            if (!intersection.IsEmpty)
            {
                var local = intersection.ToLocal(_monitor, Scale);
                var destination = new Rect(local.X, local.Y, local.Width, local.Height);
                var source = new Rect(intersection.X - _monitor.X, intersection.Y - _monitor.Y, intersection.Width,
                    intersection.Height);
                context.DrawImage(_bitmap, source, destination);
                var pen = new Pen(Brushes.DeepSkyBlue, 2);
                // Only draw actual selection edges, never artificial seams between monitors.
                if (intersection.X == Selected.X) context.DrawLine(pen, destination.TopLeft, destination.BottomLeft);
                if (intersection.Right == Selected.Right)
                    context.DrawLine(pen, destination.TopRight, destination.BottomRight);
                if (intersection.Y == Selected.Y) context.DrawLine(pen, destination.TopLeft, destination.TopRight);
                if (intersection.Bottom == Selected.Bottom)
                    context.DrawLine(pen, destination.BottomLeft, destination.BottomRight);
            }

            if (_monitor.Contains(Mouse))
            {
                var x = (Mouse.X - _monitor.X) / Scale;
                var y = (Mouse.Y - _monitor.Y) / Scale;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)));
                context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
                context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
                DrawHint(context, new Point(x, y));
            }
        }
    }
}
