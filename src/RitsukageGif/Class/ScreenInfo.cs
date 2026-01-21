using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RitsukageGif.Native;

namespace RitsukageGif.Class
{
    public class ScreenInfo
    {
        private static readonly List<ScreenInfo> ScreenInfoRecord = [];

        private ScreenInfo(Screen screen)
        {
            Screen = screen;
            UpdateDpiScale();
        }

        public static ScreenInfo MainScreen => GetScreenInfo(Screen.PrimaryScreen!);

        public Screen Screen { get; }

        public string DeviceName => Screen.DeviceName;

        public int BitsPerPixel => Screen.BitsPerPixel;

        public Rectangle Bounds => Screen.Bounds;

        public Rectangle WorkingArea => Screen.WorkingArea;

        public bool Primary => Screen.Primary;

        public double DpiScaleX { get; private set; }

        public double DpiScaleY { get; private set; }

        public double ConvertScaleX => DpiScaleToBasicX * DpiScaleToMainX;

        public double ConvertScaleY => DpiScaleToBasicY * DpiScaleToMainY;

        private static double MainDpiScaleX => MainScreen.DpiScaleX;

        private static double MainDpiScaleY => MainScreen.DpiScaleY;

        private double DpiScaleToMainX => DpiScaleX / MainDpiScaleX;

        private double DpiScaleToMainY => DpiScaleY / MainDpiScaleY;

        private double DpiScaleToBasicX => 1 / DpiScaleX;

        private double DpiScaleToBasicY => 1 / DpiScaleY;

        public void UpdateDpiScale()
        {
            Screen.GetDpi(DpiType.Effective, out var dpiX, out var dpiY);
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
            return new((float)(x / ConvertScaleX), (float)(y / ConvertScaleY));
        }

        public Point ConvertFromScalePoint(double x, double y)
        {
            return new((int)(x * ConvertScaleX), (int)(y * ConvertScaleY));
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
            return new((float)(x / ConvertScaleX), (float)(y / ConvertScaleY),
                (float)(width / ConvertScaleX), (float)(height / ConvertScaleY));
        }

        public Rectangle ConvertFromScaleRectangle(double x, double y, double width, double height)
        {
            return new((int)(x * ConvertScaleX), (int)(y * ConvertScaleY),
                (int)(width * ConvertScaleX), (int)(height * ConvertScaleY));
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
            var info = ScreenInfoRecord.FirstOrDefault(x => x.Screen.Equals(screen));
            if (info != null) return info;
            info = new(screen);
            ScreenInfoRecord.Add(info);

            return info;
        }

        public static void ClearCache()
        {
            ScreenInfoRecord.Clear();
        }
    }

    internal static class ScreenExtensions
    {
        public static void GetDpi(this Screen screen, DpiType dpiType, out uint dpiX, out uint dpiY)
        {
            var pnt = new PointStruct(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
            var mon = User32.MonitorFromPoint(pnt, 2);
            Shcore.GetDpiForMonitor(mon, dpiType, out dpiX, out dpiY);
        }
    }
}