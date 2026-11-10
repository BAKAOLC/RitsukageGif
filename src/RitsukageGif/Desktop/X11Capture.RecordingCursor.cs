using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class X11Capture
    {
        [LibraryImport("libX11.so.6", EntryPoint = "XQueryPointer")]
        private static partial int XRecordingQueryPointer(nint display, nuint window, out nuint root,
            out nuint child, out int rootX, out int rootY, out int windowX, out int windowY, out uint state);

        internal sealed class RecordingCursorReader : IDisposable
        {
            private readonly nint _display = XOpenDisplay(0);
            private RecordingCursorSample _shape = RecordingCursorSample.Hidden;

            internal RecordingCursorReader()
            {
                if (_display == 0) throw new IOException("Cannot connect to the X11 cursor source.");
            }

            public void Dispose()
            {
                _ = XCloseDisplay(_display);
            }

            internal unsafe RecordingCursorSample Read(bool includeButtons)
            {
                var pointer = XFixesGetCursorImage(_display);
                if (pointer == 0) return HiddenAtPointer(includeButtons);
                try
                {
                    var image = Marshal.PtrToStructure<CursorImage>(pointer);
                    if (image.Pixels == 0 || image.Width == 0 || image.Height == 0)
                        return HiddenAtPointer(includeButtons);
                    if (image.Width > 4096 || image.Height > 4096)
                        throw new InvalidDataException("Invalid X11 recording cursor dimensions.");
                    if (_shape.ShapeId != image.Serial || _shape.Pixels.Length == 0)
                    {
                        var pixels = new byte[checked(image.Width * image.Height * 4)];
                        for (var offset = 0; offset < pixels.Length; offset += 4)
                        {
                            var value = (uint)((nuint*)image.Pixels)[offset / 4];
                            var alpha = (byte)(value >> 24);
                            for (var channel = 0; channel < 3; channel++)
                                pixels[offset + channel] = alpha == 0
                                    ? (byte)0
                                    : (byte)Math.Min(255, ((byte)(value >> (channel * 8)) * 255 + alpha / 2) / alpha);
                            pixels[offset + 3] = alpha;
                        }

                        _shape = new(image.Serial, true, image.X, image.Y, image.Width, image.Height,
                            image.XHot, image.YHot, 1, pixels);
                    }

                    var buttons = 0;
                    if (includeButtons && XRecordingQueryPointer(_display, XDefaultRootWindow(_display), out _,
                            out _, out _, out _, out _, out _, out var state) != 0)
                        buttons = ((state & 256) != 0 ? 1 : 0) | ((state & 1024) != 0 ? 2 : 0) |
                                  ((state & 512) != 0 ? 4 : 0);
                    return _shape with { X = image.X, Y = image.Y, Buttons = buttons };
                }
                finally
                {
                    _ = XFree(pointer);
                }
            }

            private RecordingCursorSample HiddenAtPointer(bool includeButtons)
            {
                if (XRecordingQueryPointer(_display, XDefaultRootWindow(_display), out _, out _,
                        out var x, out var y, out _, out _, out var state) == 0)
                    return RecordingCursorSample.Hidden;
                return RecordingCursorSample.Hidden with
                {
                    X = x, Y = y,
                    Buttons = includeButtons
                        ? ((state & 256) != 0 ? 1 : 0) |
                          ((state & 1024) != 0 ? 2 : 0) | ((state & 512) != 0 ? 4 : 0)
                        : 0,
                };
            }
        }
    }
}
