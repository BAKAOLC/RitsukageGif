using CaptureGif.Native;
using System;
using System.Drawing;

namespace CaptureGif
{
    public class ScreenFrameProvider : IDisposable
    {
        public int Width { get; }

        public int Height { get; }

        private readonly Rectangle _rectangle;
        private readonly IntPtr _hdcSrc;
        private readonly GdiDeviceContext _dcTarget;

        private class GdiDeviceContext : IDisposable
        {
            readonly IntPtr _hdcDest, _hBitmap;

            public GdiDeviceContext(IntPtr srcDc, int width, int height)
            {
                _hdcDest = Gdi32.CreateCompatibleDC(srcDc);
                _hBitmap = Gdi32.CreateCompatibleBitmap(srcDc, width, height);
                Gdi32.SelectObject(_hdcDest, _hBitmap);
            }

            public void Dispose()
            {
                Gdi32.DeleteDC(_hdcDest);
                Gdi32.DeleteObject(_hBitmap);
            }

            public IntPtr GetDc() => _hdcDest;

            public Bitmap GetBitmap()
            {
                return Image.FromHbitmap(_hBitmap);
            }
        }

        public ScreenFrameProvider(Rectangle rectangle)
        {
            _rectangle = rectangle;
            Width = rectangle.Size.Width;
            Height = rectangle.Size.Height;
            _hdcSrc = User32.GetDC(IntPtr.Zero);
            _dcTarget = new GdiDeviceContext(_hdcSrc, Width, Height);
        }

        private void OnCapture(bool cursor = false)
        {
            Rectangle rect = _rectangle;
            IntPtr hdcDest = _dcTarget.GetDc();
            Gdi32.StretchBlt(hdcDest, 0, 0, Width, Height, _hdcSrc, rect.X, rect.Y, Width, Height, (int)CopyPixelOperation.SourceCopy);
            if (cursor)
            {
                Cursor.Draw(hdcDest, p => new Point(p.X - _rectangle.X, p.Y - _rectangle.Y));
            }
        }

        public Bitmap Capture(bool cursor = false)
        {
            OnCapture(cursor);
            Bitmap img = _dcTarget.GetBitmap();
            return img;
        }

        public void Dispose()
        {
            _dcTarget.Dispose();
            User32.ReleaseDC(IntPtr.Zero, _hdcSrc);
        }
    }
}
