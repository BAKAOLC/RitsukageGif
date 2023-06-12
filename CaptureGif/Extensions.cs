using System;
using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CaptureGif
{

    internal static class Extensions
    {
        public static BitmapSource ToBitmapSource(this Bitmap bitmap)
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap.GetHbitmap(),
                IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        public static bool Contains(this Rectangle rect, double x, double y)
        {
            if (rect.X <= x && x < rect.X + rect.Width && rect.Y <= y)
            {
                return y < rect.Y + rect.Height;
            }
            return false;
        }

        public static bool Contains(this Rectangle rect, PointF pt)
        {
            return rect.Contains(pt.X, pt.Y);
        }

        public static bool Contains(this Rectangle rect, RectangleF rectF)
        {
            if (rect.X <= rectF.X && rectF.X + rectF.Width <= rect.X + rect.Width && rect.Y <= rectF.Y)
            {
                return rectF.Y + rectF.Height <= rect.Y + rect.Height;
            }

            return false;
        }
    }
}
