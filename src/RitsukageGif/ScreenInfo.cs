using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RitsukageGif
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

        public double ConvertScaleX => _dpiScaleToBasicX * _dpiScaleToMainX;

        public double ConvertScaleY => _dpiScaleToBasicY * _dpiScaleToMainY;
        
        private double _mainDpiScaleX => MainScreen.DpiScaleX;

        private double _mainDpiScaleY => MainScreen.DpiScaleY;

        private double _dpiScaleToMainX => DpiScaleX / _mainDpiScaleX;

        private double _dpiScaleToMainY => DpiScaleY / _mainDpiScaleY;

        private double _dpiScaleToBasicX => 1 / DpiScaleX;

        private double _dpiScaleToBasicY => 1 / DpiScaleY;

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
        
        public RectangleF GetConvertedIntersectionRegion(Rectangle rect, bool needConvert = true)
        {
            var rectF = needConvert ? ConvertToScaleRectangle(rect) : rect;
            rectF.Intersect(Screen.Bounds);
            return rectF;
        }

        public PointF ConvertToScalePoint(Point point)
        {
            return ConvertToScalePoint(point.X, point.Y);
        }

        public Point ConvertFromScalePoint(PointF point)
        {
            return ConvertFromScalePoint(point.X, point.Y);
        }

        public PointF ConvertToScalePoint(double x, double y)
        {
            return new PointF((float)(x / ConvertScaleX), (float)(y / ConvertScaleY));
        }

        public Point ConvertFromScalePoint(double x, double y)
        {
            return new Point((int)(x * ConvertScaleX), (int)(y * ConvertScaleY));
        }

        public RectangleF ConvertToScaleRectangle(Rectangle rect)
        {
            return ConvertToScaleRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }

        public Rectangle ConvertFromScaleRectangle(RectangleF rect)
        {
            return ConvertFromScaleRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }

        public RectangleF ConvertToScaleRectangle(double x, double y, double width, double height)
        {
            return new RectangleF((float)(x / ConvertScaleX), (float)(y / ConvertScaleY), (float)(width /
                ConvertScaleX), (float)(height / ConvertScaleY));
        }

        public Rectangle ConvertFromScaleRectangle(double x, double y, double width, double height)
        {
            return new Rectangle((int)(x * ConvertScaleX), (int)(y * ConvertScaleY), (int)(width *
                ConvertScaleX), (int)(height * ConvertScaleY));
        }

        public float ConvertToScaleX(double x)
        {
            return (float)(x / ConvertScaleX);
        }

        public int ConvertFromScaleX(double x)
        {
            return (int)(x * ConvertScaleX);
        }

        public float ConvertToScaleY(double y)
        {
            return (float)(y / ConvertScaleY);
        }

        public int ConvertFromScaleY(double y)
        {
            return (int)(y * ConvertScaleY);
        }

        public override string ToString()
        {
            return DeviceName;
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
