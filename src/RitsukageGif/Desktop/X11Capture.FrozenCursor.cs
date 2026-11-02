using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class X11Capture
    {
        internal static unsafe FrozenScreenshotCursor? CaptureFrozenCursor()
        {
            var display = XOpenDisplay(0);
            if (display == 0) throw new IOException(UiText.Get("CaptureFailed"));
            try
            {
                var pointer = XFixesGetCursorImage(display);
                if (pointer == 0) return null;
                try
                {
                    var image = Marshal.PtrToStructure<CursorImage>(pointer);
                    if (image.Pixels == 0 || image.Width == 0 || image.Height == 0 || image.Width > 4096 ||
                        image.Height > 4096) return null;
                    var bytes = new byte[checked(image.Width * image.Height * 4)];
                    for (var i = 0; i < bytes.Length; i += 4)
                    {
                        var value = (uint)((nuint*)image.Pixels)[i / 4];
                        var alpha = (byte)(value >> 24);
                        for (var c = 0; c < 3; c++)
                            bytes[i + c] = alpha == 0
                                ? (byte)0
                                : (byte)Math.Min(255, ((byte)(value >> (c * 8)) * 255 + alpha / 2) / alpha);
                        bytes[i + 3] = alpha;
                    }

                    return FrozenScreenshotCursor.FromNative(new()
                    {
                        Valid = true, Visible = true, Width = image.Width, Height = image.Height,
                        HotspotX = image.XHot, HotspotY = image.YHot, X = image.X, Y = image.Y, PixelScale = 1,
                    }, bytes);
                }
                finally
                {
                    _ = XFree(pointer);
                }
            }
            finally
            {
                _ = XCloseDisplay(display);
            }
        }
    }
}
