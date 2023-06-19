using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    internal class Cursor
    {
        const int CursorShowing = 1;

        public static void Draw(Graphics g, Func<Point, Point> transform = null)
        {
            var hIcon = GetIcon(transform, out var location);
            if (hIcon == IntPtr.Zero)
                return;
            using (var bmp = Icon.FromHandle(hIcon).ToBitmap())
            {
                User32.DestroyIcon(hIcon);
                try
                {
                    g.DrawImage(bmp, new Rectangle(location, bmp.Size));
                }
                catch (ArgumentException)
                {
                }
            }
        }

        public static void Draw(IntPtr deviceContext, Func<Point, Point> transform = null)
        {
            var hIcon = GetIcon(transform, out var location);
            if (hIcon == IntPtr.Zero)
                return;
            try
            {
                User32.DrawIconEx(deviceContext, location.X, location.Y, hIcon, 0, 0, 0, IntPtr.Zero,
                    DrawIconExFlags.Normal);
            }
            finally
            {
                User32.DestroyIcon(hIcon);
            }
        }

        static IntPtr GetIcon(Func<Point, Point> transform, out Point location)
        {
            location = Point.Empty;
            var cursorInfo = new CursorInfo { cbSize = Marshal.SizeOf<CursorInfo>() };
            if (!User32.GetCursorInfo(ref cursorInfo))
                return IntPtr.Zero;
            if (cursorInfo.flags != CursorShowing)
                return IntPtr.Zero;
            var hIcon = User32.CopyIcon(cursorInfo.hCursor);
            if (hIcon == IntPtr.Zero)
                return IntPtr.Zero;
            if (!User32.GetIconInfo(hIcon, out var icInfo))
                return IntPtr.Zero;
            var hotspot = new Point(icInfo.xHotspot, icInfo.yHotspot);
            location = new Point(cursorInfo.ptScreenPos.X - hotspot.X, cursorInfo.ptScreenPos.Y - hotspot.Y);
            if (transform != null)
                location = transform(location);
            Gdi32.DeleteObject(icInfo.hbmColor);
            Gdi32.DeleteObject(icInfo.hbmMask);
            return hIcon;
        }
    }
}