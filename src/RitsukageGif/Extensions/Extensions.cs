using System;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace RitsukageGif.Extensions
{
    internal static class Extensions
    {
        public static BitmapSource ToBitmapSource(this Bitmap bitmap)
        {
            return Imaging.CreateBitmapSourceFromHBitmap(bitmap.GetHbitmap(),
                IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        extension(Rectangle rect)
        {
            public bool Contains(double x, double y)
            {
                return rect.X <= x && x < rect.X + rect.Width && rect.Y <= y && y < rect.Y + rect.Height;
            }

            public bool Contains(PointF pt)
            {
                return rect.Contains(pt.X, pt.Y);
            }

            public bool Contains(RectangleF rectF)
            {
                return rect.X <= rectF.X && rectF.X + rectF.Width <= rect.X + rect.Width && rect.Y <= rectF.Y
                       && rectF.Y + rectF.Height <= rect.Y + rect.Height;
            }
        }
    }
}