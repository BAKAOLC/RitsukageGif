using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingMaskWindow : Window
    {
        private static readonly ILogger MaskLogger = LogHub.GetLogger("Desktop.Capture.ScrollingMask");
        private readonly double _borderWidth;
        private readonly ScreenshotScrollingMaskSurface _surface;

        public ScreenshotScrollingMaskWindow(AppearancePreferences appearance)
        {
            SystemDecorations = SystemDecorations.None;
            ShowActivated = false;
            Topmost = true;
            CanResize = false;
            Background = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            MinWidth = 0;
            MinHeight = 0;
            TransientWindowShell.Configure(this);
            _surface = new(appearance);
            _borderWidth = appearance.SelectionBorderWidth;
            Content = _surface;
        }

        public void Update(PixelRect monitor, PixelRect capture, double scale, nint toolbar)
        {
            var firstShow = !IsVisible;
            Position = new(monitor.X, monitor.Y);
            Width = monitor.Width / scale;
            Height = monitor.Height / scale;
            _surface.Update(monitor, capture, scale, false);
            if (!IsVisible) Show();
            var excluded = false;
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle)
            {
                var style = GetWindowStyle(handle.Handle, -20);
                Marshal.SetLastPInvokeError(0);
                if (SetWindowStyle(handle.Handle, -20, style | 0x08000000 | 0x80 | 0x20) == 0 &&
                    Marshal.GetLastPInvokeError() != 0)
                    Trace.TraceWarning("Scrolling mask input style: {0}", Marshal.GetLastPInvokeError());
                excluded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                           SetWindowDisplayAffinity(handle.Handle, 0x11);
                _ = NativeMethods.SetWindowPos(handle.Handle, toolbar != 0 ? toolbar : -1, monitor.X, monitor.Y,
                    monitor.Width,
                    monitor.Height, 0x0010);
                ApplyShape(handle.Handle, monitor, capture, excluded, (int)Math.Ceiling(_borderWidth * RenderScaling));
            }

            _surface.Update(monitor, capture, RenderScaling, excluded);
            if (firstShow)
            {
                MaskLogger.ZLogInformation(
                    $"Scrolling mask initialized. Monitor={monitor} Capture={capture} Scaling={RenderScaling} CaptureExcluded={excluded}");
                WindowDiagnostics.Write(MaskLogger, "ScrollingMaskReady", this);
            }
        }

        private static void ApplyShape(nint window, PixelRect monitor, PixelRect capture, bool excluded, int thickness)
        {
            var region = CreateRectRegion(0, 0, monitor.Width, monitor.Height);
            if (region == 0) return;
            var hole = capture.Intersect(monitor);
            if (!hole.IsEmpty)
            {
                var left = hole.X;
                var top = hole.Y;
                var right = hole.Right;
                var bottom = hole.Bottom;
                if (excluded)
                {
                    if (capture.X == monitor.X) left = Math.Min(right, left + thickness);
                    if (capture.Y == monitor.Y) top = Math.Min(bottom, top + thickness);
                    if (capture.Right == monitor.Right) right = Math.Max(left, right - thickness);
                    if (capture.Bottom == monitor.Bottom) bottom = Math.Max(top, bottom - thickness);
                }

                var cutout = CreateRectRegion(left - monitor.X, top - monitor.Y, right - monitor.X,
                    bottom - monitor.Y);
                if (cutout == 0)
                {
                    _ = DeleteRegion(region);
                    return;
                }

                var combined = CombineRegion(region, region, cutout, 4);
                _ = DeleteRegion(cutout);
                if (combined == 0)
                {
                    _ = DeleteRegion(region);
                    return;
                }
            }

            if (SetWindowRegion(window, region, 1) == 0) _ = DeleteRegion(region);
        }

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static partial nint GetWindowStyle(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static partial nint SetWindowStyle(nint window, int index, nint value);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowDisplayAffinity(nint window, uint affinity);

        [LibraryImport("gdi32.dll", EntryPoint = "CreateRectRgn")]
        private static partial nint CreateRectRegion(int left, int top, int right, int bottom);

        [LibraryImport("gdi32.dll", EntryPoint = "CombineRgn")]
        private static partial int CombineRegion(nint destination, nint source, nint difference, int mode);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowRgn")]
        private static partial int SetWindowRegion(nint window, nint region, int redraw);

        [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteRegion(nint region);
    }
}
