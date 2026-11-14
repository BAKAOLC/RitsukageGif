using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Win32.Win32Com;
using RitsukageGif.DragPreview;
using static Avalonia.Win32.Interop.UnmanagedMethods;

namespace Avalonia.Win32
{
    internal sealed partial class Win32DragPreview : IDisposable
    {
        private readonly Dictionary<DropEffect, IntPtr> _cursors = new();

        private Win32DragPreview(IDragPreview preview, DropEffect allowed)
        {
            using var snapshot = new RetainedFramebuffer(preview.Preview.PixelSize, PixelFormats.Bgra8888,
                AlphaFormat.Premul);
            using var frame = snapshot.Lock(preview.Preview.Dpi, _ => { });
            preview.Preview.CopyPixels(frame);
            try
            {
                Add(DropEffect.None, StandardCursorType.No);
                if ((allowed & DropEffect.Copy) != 0) Add(DropEffect.Copy, StandardCursorType.DragCopy);
                if ((allowed & DropEffect.Move) != 0) Add(DropEffect.Move, StandardCursorType.DragMove);
                if ((allowed & DropEffect.Link) != 0) Add(DropEffect.Link, StandardCursorType.DragLink);
            }
            catch
            {
                Dispose();
                throw;
            }

            return;

            void Add(DropEffect effect, StandardCursorType cursorType)
            {
                var cursor = (CursorImpl)CursorFactory.Instance.GetCursor(cursorType);
                _cursors.Add(effect, Create(frame, preview.HotSpot, cursor.Handle));
            }
        }

        public void Dispose()
        {
            if (_cursors.ContainsValue(GetCursor()))
            {
                var arrow = (CursorImpl)CursorFactory.Instance.GetCursor(StandardCursorType.Arrow);
                _ = SetCursor(arrow.Handle);
            }

            foreach (var cursor in _cursors.Values) _ = DestroyCursor(cursor);
            _cursors.Clear();
        }

        public static Win32DragPreview? TryCreate(IDragPreview preview, DropEffect allowed)
        {
            try
            {
                return new(preview, allowed);
            }
            catch (Win32Exception)
            {
                return null;
            }
        }

        public IntPtr GetCursor(DropEffect effect)
        {
            if ((effect & DropEffect.Copy) != 0 && _cursors.TryGetValue(DropEffect.Copy, out var copy)) return copy;
            if ((effect & DropEffect.Move) != 0 && _cursors.TryGetValue(DropEffect.Move, out var move)) return move;
            if ((effect & DropEffect.Link) != 0 && _cursors.TryGetValue(DropEffect.Link, out var link)) return link;
            return _cursors[DropEffect.None];
        }

        private static unsafe IntPtr Create(ILockedFramebuffer preview, PixelPoint hotSpot, IntPtr effectCursor)
        {
            if (GetIconInfo(effectCursor, out var effect) == 0) throw new Win32Exception();
            var width = Math.Max(preview.Size.Width, hotSpot.X + GetSystemMetrics(SystemMetric.SM_CXCURSOR));
            var height = Math.Max(preview.Size.Height, hotSpot.Y + GetSystemMetrics(SystemMetric.SM_CYCURSOR));
            var header = new BITMAPINFOHEADER
            {
                biSize = 40,
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
            };
            var color = CreateDIBSection(IntPtr.Zero, ref header, DIBColorTable.DIB_RGB_COLORS, out var pixels,
                IntPtr.Zero, 0);
            var mask = IntPtr.Zero;
            var dc = IntPtr.Zero;
            var previous = IntPtr.Zero;
            try
            {
                if (color == IntPtr.Zero) throw new Win32Exception();
                var colorStride = width * 4;
                var dib = new Span<byte>((void*)pixels, colorStride * height);
                dib.Clear();
                for (var row = 0; row < preview.Size.Height; row++)
                    new ReadOnlySpan<byte>((byte*)preview.Address + row * preview.RowBytes, preview.Size.Width * 4)
                        .CopyTo(dib[(row * colorStride)..]);

                dc = CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) throw new Win32Exception();
                previous = SelectObject(dc, color);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1) ||
                    DrawIconEx(dc, hotSpot.X - (int)effect.X, hotSpot.Y - (int)effect.Y,
                        effectCursor, 0, 0, 0, IntPtr.Zero, 3) == 0) throw new Win32Exception();
                _ = GdiFlush();
                mask = CreateMask(dib, width, height, colorStride);
                if (mask == IntPtr.Zero) throw new Win32Exception();
                var icon = new ICONINFO
                {
                    IsIcon = false,
                    xHotspot = hotSpot.X,
                    yHotspot = hotSpot.Y,
                    MaskBitmap = mask,
                    ColorBitmap = color,
                };
                var result = CreateIconIndirect(ref icon);
                if (result == IntPtr.Zero) throw new Win32Exception();
                return result;
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) _ = SelectObject(dc, previous);
                if (dc != IntPtr.Zero) _ = DeleteDC(dc);
                if (mask != IntPtr.Zero) _ = DeleteObject(mask);
                if (color != IntPtr.Zero) _ = DeleteObject(color);
                if (effect.Mask != IntPtr.Zero) _ = DeleteObject(effect.Mask);
                if (effect.Color != IntPtr.Zero) _ = DeleteObject(effect.Color);
            }
        }

        private static unsafe IntPtr CreateMask(ReadOnlySpan<byte> color, int width, int height, int colorStride)
        {
            var maskStride = (width + 15) / 16 * 2;
            var mask = new byte[maskStride * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (color[y * colorStride + x * 4 + 3] == 0)
                    mask[y * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));

            fixed (byte* bytes = mask)
                return CreateBitmap(width, height, 1, 1, (IntPtr)bytes);
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int GetIconInfo(IntPtr cursor, out CursorInfo info);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height,
            uint step, IntPtr brush, uint flags);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetCursor();

        [LibraryImport("user32.dll")]
        private static partial int DestroyCursor(IntPtr cursor);

        [LibraryImport("gdi32.dll")]
        private static partial int DeleteDC(IntPtr dc);

        [LibraryImport("gdi32.dll")]
        private static partial int GdiFlush();

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public int IsIcon;
            public uint X;
            public uint Y;
            public IntPtr Mask;
            public IntPtr Color;
        }
    }
}
