using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CaptureGif
{
    public class ScreenInfo
    {
        private static readonly List<ScreenInfo> _screenInfoRecord = new List<ScreenInfo>();

        public static ScreenInfo MainScreen => GetScreenInfo(Screen.FromPoint(default));

        public Screen Screen { get; }

        public string DeviceName => Screen.DeviceName;

        public int BitsPerPixel => Screen.BitsPerPixel;

        public Rectangle Bounds => Screen.Bounds;

        public Rectangle WorkingArea => Screen.WorkingArea;

        public bool Primary => Screen.Primary;

        public double DpiScaleX { get; private set; }

        public double DpiScaleY { get; private set; }

        private ScreenInfo(Screen screen)
        {
            Screen = screen;
            UpdateDpiScale();
        }

        public void UpdateDpiScale()
        {
            Screen.GetDpi(DpiType.Effective, out uint dpiX, out uint dpiY);
            DpiScaleX = dpiX / 96.0;
            DpiScaleY = dpiY / 96.0;
        }

        public static ScreenInfo GetScreenInfo(Screen screen)
        {
            var info = _screenInfoRecord.FirstOrDefault(x => x.Screen.Equals(screen));
            if (info == null)
            {
                info = new ScreenInfo(screen);
                _screenInfoRecord.Add(info);
            }
            return info;
        }

        public static void ClearCache()
        {
            _screenInfoRecord.Clear();
        }
    }

    internal static class ScreenExtensions
    {
        public static void GetDpi(this Screen screen, DpiType dpiType, out uint dpiX, out uint dpiY)
        {
            var pnt = new Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
            var mon = MonitorFromPoint(pnt, 2);
            GetDpiForMonitor(mon, dpiType, out dpiX, out dpiY);
        }

        [DllImport("User32.dll")]
        private static extern IntPtr MonitorFromPoint([In] Point pt, [In] uint dwFlags);

        [DllImport("Shcore.dll")]
        private static extern IntPtr GetDpiForMonitor([In] IntPtr hmonitor, [In] DpiType dpiType, [Out] out uint dpiX, [Out] out uint dpiY);
    }

    internal enum DpiType
    {
        Effective = 0,
        Angular = 1,
        Raw = 2,
    }
}
