using System;
using System.Collections.Generic;
using System.Drawing;
using RitsukageGif.Native;

namespace RitsukageGif.Class
{
    internal class WinWindow
    {
        public WinWindow(IntPtr handle)
        {
            if (!User32.IsWindow(handle)) throw new ArgumentException("Invalid window handle", nameof(handle));

            Handle = handle;
        }

        public IntPtr Handle { get; }

        public bool IsAlive => User32.IsWindow(Handle);

        public bool IsVisible => User32.IsWindowVisible(Handle);

        public Rectangle Bounds
        {
            get
            {
                var r = new RectStruct();
                const int extendedFrameBounds = 9;
                if (DwmApi.DwmGetWindowAttribute(Handle, extendedFrameBounds, ref r) == 0)
                    return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                return !User32.GetWindowRect(Handle, out r)
                    ? Rectangle.Empty
                    : new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
        }

        public IEnumerable<WinWindow> EnumerateChildren()
        {
            var list = new List<WinWindow>();
            User32.EnumChildWindows(Handle, (handle, _) =>
            {
                var wh = new WinWindow(handle);
                list.Add(wh);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public override bool Equals(object? obj)
        {
            return obj is WinWindow w && w.Handle == Handle;
        }

        public override int GetHashCode()
        {
            return Handle.GetHashCode();
        }

        public static bool operator ==(WinWindow w1, WinWindow w2)
        {
            return w1?.Handle == w2?.Handle;
        }

        public static bool operator !=(WinWindow w1, WinWindow w2)
        {
            return !(w1 == w2);
        }

        public static IEnumerable<WinWindow> Enumerate()
        {
            var list = new List<WinWindow>();
            User32.EnumWindows((handle, param) =>
            {
                var wh = new WinWindow(handle);
                list.Add(wh);
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}