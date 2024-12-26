using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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

        public bool IsMinimized => User32.IsIconic(Handle);

        public bool IsMaximized => User32.IsZoomed(Handle);

        public string Title
        {
            get
            {
                var title = new StringBuilder(User32.GetWindowTextLength(Handle) + 1);
                User32.GetWindowText(Handle, title, title.Capacity);
                return title.ToString();
            }
        }

        public Rectangle Bounds
        {
            get
            {
                var r = new Rect();
                const int extendedFrameBounds = 9;
                if (DwmApi.DwmGetWindowAttribute(Handle, extendedFrameBounds, ref r, Marshal.SizeOf<Rect>()) == 0)
                    return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                return !User32.GetWindowRect(Handle, out r)
                    ? Rectangle.Empty
                    : new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
        }

        public static WinWindow DesktopWindow { get; } = new(User32.GetDesktopWindow());

        public static WinWindow ForegroundWindow => new(User32.GetForegroundWindow());

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

        public override string ToString()
        {
            return Title;
        }

        public override bool Equals(object obj)
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

        public static IEnumerable<WinWindow> EnumerateVisible()
        {
            foreach (var window in Enumerate().Where(w => w.IsVisible && !string.IsNullOrWhiteSpace(w.Title)))
            {
                var hWnd = window.Handle;
                if (!User32.GetWindowLong(hWnd, GetWindowLongValue.ExStyle).HasFlag(WindowStyles.AppWindow))
                {
                    if (User32.GetWindow(hWnd, GetWindowEnum.Owner) != IntPtr.Zero)
                        continue;

                    if (User32.GetWindowLong(hWnd, GetWindowLongValue.ExStyle).HasFlag(WindowStyles.ToolWindow))
                        continue;

                    if (User32.GetWindowLong(hWnd, GetWindowLongValue.Style).HasFlag(WindowStyles.Child))
                        continue;
                }

                const int dwmCloaked = 14;
                DwmApi.DwmGetWindowAttribute(hWnd, dwmCloaked, out var cloaked, Marshal.SizeOf<bool>());
                if (cloaked)
                    continue;
                yield return window;
            }
        }
    }
}