using RitsukageGif.Native;
using System.Drawing;

namespace RitsukageGif
{
    public class ScreenFrameProvider
    {
        public Rectangle Rectangle { get; }

        public ScreenFrameProvider(Rectangle rectangle)
        {
            Rectangle = rectangle;
        }

        public Bitmap Capture(bool cursor = false, double scale = 1)
        {
            var width = (int)(Rectangle.Width * scale);
            var height = (int)(Rectangle.Height * scale);
            var bitmap = new Bitmap(width, height);
            using (var source = new Bitmap(Rectangle.Width, Rectangle.Height))
            using (var graphicsBitmap = Graphics.FromImage(bitmap))
            using (var graphicsSource = Graphics.FromImage(source))
            {
                graphicsSource.CopyFromScreen(Rectangle.X, Rectangle.Y, 0, 0, Rectangle.Size);
                if (cursor)
                {
                    Cursor.Draw(graphicsSource, p => new Point(p.X - Rectangle.X, p.Y - Rectangle.Y));
                }

                graphicsBitmap.DrawImage(source, 0, 0, width, height);
            }

            return bitmap;
        }
    }
}