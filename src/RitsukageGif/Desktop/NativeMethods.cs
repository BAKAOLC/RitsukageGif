using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial nint GetDC(nint window);

        [LibraryImport("user32.dll")]
        internal static partial int ReleaseDC(nint window, nint dc);

        [LibraryImport("gdi32.dll")]
        internal static partial nint CreateCompatibleDC(nint dc);

        [LibraryImport("gdi32.dll")]
        internal static partial int DeleteDC(nint dc);

        [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
        internal static partial nint CreateDibSection(nint dc, ref BitmapInfo info, uint usage, out nint bits,
            nint section,
            uint offset);

        [LibraryImport("gdi32.dll")]
        internal static partial nint SelectObject(nint dc, nint obj);

        [LibraryImport("gdi32.dll")]
        internal static partial int DeleteObject(nint obj);

        [LibraryImport("gdi32.dll")]
        internal static partial int BitBlt(nint target, int x, int y, int width, int height, nint source, int sx,
            int sy,
            uint operation);

        [LibraryImport("gdi32.dll")]
        internal static partial int StretchBlt(nint target, int x, int y, int width, int height, nint source, int sx,
            int sy, int sourceWidth, int sourceHeight, uint operation);

        [LibraryImport("gdi32.dll")]
        internal static partial int SetStretchBltMode(nint dc, int mode);

        [LibraryImport("gdi32.dll")]
        internal static partial int SetBrushOrgEx(nint dc, int x, int y, nint old);

        [LibraryImport("gdi32.dll")]
        internal static partial int GdiFlush();

        [LibraryImport("user32.dll")]
        internal static partial int GetCursorPos(out Point point);

        [LibraryImport("user32.dll")]
        internal static partial int GetCursorInfo(ref CursorInfo info);

        [LibraryImport("user32.dll")]
        internal static partial int GetIconInfo(nint icon, out IconInfo info);

        [LibraryImport("user32.dll")]
        internal static partial int DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step,
            nint brush, uint flags);

        [LibraryImport("user32.dll")]
        internal static partial short GetAsyncKeyState(int key);

        [LibraryImport("user32.dll")]
        internal static partial nint GetTopWindow(nint window);

        [LibraryImport("user32.dll")]
        internal static partial nint GetWindow(nint window, uint command);

        [LibraryImport("user32.dll")]
        internal static partial int IsWindowVisible(nint window);

        [LibraryImport("user32.dll")]
        internal static partial int IsIconic(nint window);

        [LibraryImport("user32.dll")]
        internal static partial int GetWindowRect(nint window, out Rect rect);

        [LibraryImport("dwmapi.dll")]
        internal static partial int DwmGetWindowAttribute(nint window, uint attribute, out Rect rect, int size);

        [LibraryImport("user32.dll")]
        internal static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height,
            uint flags);

        [LibraryImport("user32.dll")]
        internal static partial int RegisterHotKey(nint window, int id, uint modifiers, uint key);

        [LibraryImport("user32.dll")]
        internal static partial int UnregisterHotKey(nint window, int id);

        [LibraryImport("user32.dll")]
        internal static partial int PeekMessageW(out Message message, nint window, uint min, uint max, uint remove);

        public static PixelPoint Mouse()
        {
            if (GetCursorPos(out var point) == 0) throw new InvalidOperationException(UiText.Get("CursorFailed"));
            return new PixelPoint(point.X, point.Y);
        }

        public static List<PixelRect> VisibleWindows()
        {
            var result = new List<PixelRect>();
            for (var hwnd = GetTopWindow(0); hwnd != 0; hwnd = GetWindow(hwnd, 2))
            {
                if (IsWindowVisible(hwnd) == 0 || IsIconic(hwnd) != 0) continue;
                if (DwmGetWindowAttribute(hwnd, 9, out var rect, 16) != 0 &&
                    GetWindowRect(hwnd, out rect) == 0) continue;
                if (!rect.Pixels.IsEmpty) result.Add(rect.Pixels);
            }

            return result;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left, Top, Right, Bottom;
            public readonly PixelRect Pixels => new(Left, Top, Right - Left, Bottom - Top);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfo
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, ImageSize;
            public int XPels, YPels;
            public uint Used, Important;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CursorInfo
        {
            public int Size, Flags;
            public nint Cursor;
            public Point Position;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IconInfo
        {
            public int Icon;
            public uint XHotspot, YHotspot;
            public nint Mask, Color;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Message
        {
            public nint Window;
            public uint Id;
            public nuint WParam;
            public nint LParam;
            public uint Time;
            public Point Point;
            public uint Private;
        }
    }
}
