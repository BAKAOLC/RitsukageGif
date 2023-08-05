using System;
using RitsukageGif.Native;
using System.Drawing;
using Rectangle = System.Drawing.Rectangle;

namespace RitsukageGif.CaptureProvider.ScreenFrame
{
    public class BitbltScreenFrameProvider : IScreenFrameProvider
    {
        public const string ProviderId = "86597fcc-9b42-ea23-e5d6-f6365537e674";

        private Rectangle _rectangle;

        public BitbltScreenFrameProvider()
        {
            Initialize();
        }

        public void Initialize()
        {}

        public void ApplyCaptureRegion(Rectangle rect)
        {
            _rectangle = rect;
        }

        public Bitmap Capture(bool cursor = false, double scale = 1)
        {
            var width = (int)(_rectangle.Width * scale);
            var height = (int)(_rectangle.Height * scale);
            var bitmap = new Bitmap(width, height);
            using (var source = new Bitmap(_rectangle.Width, _rectangle.Height))
            using (var graphicsBitmap = Graphics.FromImage(bitmap))
            using (var graphicsSource = Graphics.FromImage(source))
            {
                graphicsSource.CopyFromScreen(_rectangle.X, _rectangle.Y, 0, 0, _rectangle.Size);
                if (cursor) Cursor.Draw(graphicsSource, p => new Point(p.X - _rectangle.X, p.Y - _rectangle.Y));

                graphicsBitmap.DrawImage(source, 0, 0, width, height);
            }

            return bitmap;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}