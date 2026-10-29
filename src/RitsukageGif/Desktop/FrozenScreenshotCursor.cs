using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class FrozenScreenshotCursor : IDisposable
    {
        private readonly PixelRect _bounds;
        private readonly Lock _sync = new();
        private nint _icon;

        private FrozenScreenshotCursor(nint icon, PixelRect bounds)
        {
            _icon = icon;
            _bounds = bounds;
        }

        internal PixelRect Bounds => _bounds;

        public void Dispose()
        {
            lock (_sync)
            {
                if (_icon != 0) CheckCleanup(DestroyIcon(_icon));
                _icon = 0;
            }
        }

        internal byte[]? ReadTransparentPixels()
        {
            var black = new byte[checked(_bounds.Width * _bounds.Height * 4)];
            var white = new byte[black.Length];
            Array.Fill(white, (byte)255);
            Draw(black, _bounds);
            Draw(white, _bounds);
            for (var offset = 0; offset < black.Length; offset += 4)
            {
                var difference = white[offset] - black[offset];
                // Inverting/monochrome cursors depend on the destination and cannot be represented by RGBA.
                if (difference < 0 || Math.Abs(white[offset + 1] - black[offset + 1] - difference) > 1 ||
                    Math.Abs(white[offset + 2] - black[offset + 2] - difference) > 1) return null;
                var alpha = 255 - difference;
                for (var channel = 0; channel < 3; channel++)
                    black[offset + channel] = alpha == 0
                        ? (byte)0
                        : (byte)Math.Clamp(
                            (black[offset + channel] * 255 + alpha / 2) / alpha, 0, 255);
                black[offset + 3] = (byte)alpha;
            }

            return black;
        }

        internal static FrozenScreenshotCursor? Capture()
        {
            if (!OperatingSystem.IsWindows()) return null;
            var cursor = new NativeMethods.CursorInfo { Size = Marshal.SizeOf<NativeMethods.CursorInfo>() };
            if (NativeMethods.GetCursorInfo(ref cursor) == 0 || (cursor.Flags & 1) == 0) return null;
            var copy = CopyIcon(cursor.Cursor);
            if (copy == 0) return null;
            if (NativeMethods.GetIconInfo(copy, out var info) == 0)
            {
                CheckCleanup(DestroyIcon(copy));
                return null;
            }

            try
            {
                if (GetObject(info.Color != 0 ? info.Color : info.Mask, Marshal.SizeOf<Bitmap>(), out var bitmap) == 0)
                {
                    CheckCleanup(DestroyIcon(copy));
                    return null;
                }

                var height = info.Color == 0 ? bitmap.Height / 2 : bitmap.Height;
                return new(copy, new(cursor.Position.X - (int)info.XHotspot,
                    cursor.Position.Y - (int)info.YHotspot, bitmap.Width, height));
            }
            finally
            {
                if (info.Color != 0) CheckCleanup(NativeMethods.DeleteObject(info.Color));
                if (info.Mask != 0) CheckCleanup(NativeMethods.DeleteObject(info.Mask));
            }
        }

        internal void Draw(byte[] pixels, PixelRect region)
        {
            var clipped = region.Intersect(_bounds);
            if (clipped.IsEmpty) return;
            lock (_sync)
            {
                if (_icon == 0) return;
                var dc = NativeMethods.CreateCompatibleDC(0);
                var info = new NativeMethods.BitmapInfo
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfo>(),
                    Width = clipped.Width, Height = -clipped.Height, Planes = 1, BitCount = 32,
                };
                var bitmap = NativeMethods.CreateDibSection(dc, ref info, 0, out var bits, 0, 0);
                nint old = 0;
                try
                {
                    if (dc == 0 || bitmap == 0 || bits == 0)
                        throw new InvalidOperationException(UiText.Get("CaptureBufferFailed"));
                    old = NativeMethods.SelectObject(dc, bitmap);
                    if (old is 0 or -1) throw new InvalidOperationException(UiText.Get("CaptureBufferFailed"));
                    var stride = checked(clipped.Width * 4);
                    for (var y = 0; y < clipped.Height; y++)
                        Marshal.Copy(pixels, ((clipped.Y - region.Y + y) * region.Width + clipped.X - region.X) * 4,
                            bits + y * stride, stride);
                    if (NativeMethods.DrawIconEx(dc, _bounds.X - clipped.X, _bounds.Y - clipped.Y, _icon, _bounds.Width,
                            _bounds.Height, 0, 0, 3) == 0 || NativeMethods.GdiFlush() == 0)
                        throw new InvalidOperationException(UiText.Get("CaptureBufferFailed"));
                    for (var y = 0; y < clipped.Height; y++)
                    {
                        var offset = ((clipped.Y - region.Y + y) * region.Width + clipped.X - region.X) * 4;
                        Marshal.Copy(bits + y * stride, pixels, offset, stride);
                        for (var alpha = offset + 3; alpha < offset + stride; alpha += 4) pixels[alpha] = 255;
                    }
                }
                finally
                {
                    if (old != 0 && old != -1) NativeMethods.SelectObject(dc, old);
                    if (bitmap != 0) CheckCleanup(NativeMethods.DeleteObject(bitmap));
                    if (dc != 0) CheckCleanup(NativeMethods.DeleteDC(dc));
                }
            }
        }

        private static void CheckCleanup(int result)
        {
            // Release every owned handle without hiding a capture error behind a cleanup failure.
            // ReSharper disable once InvocationIsSkipped
            if (result == 0) Debug.WriteLine("Screenshot cursor native cleanup failed.");
        }

        [LibraryImport("user32.dll")]
        private static partial nint CopyIcon(nint icon);

        [LibraryImport("user32.dll")]
        private static partial int DestroyIcon(nint icon);

        [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
        private static partial int GetObject(nint obj, int size, out Bitmap bitmap);

        [StructLayout(LayoutKind.Sequential)]
        private struct Bitmap
        {
            public int Type, Width, Height, WidthBytes;
            public ushort Planes, BitsPixel;
            public nint Bits;
        }
    }
}
