using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingPreviewWindow : Window
    {
        private const double BorderWidth = 4;
        private const double Gap = 15;
        private const double TargetShortEdge = 200;
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Capture.ScrollingPreview");
        private readonly ScreenshotScrollingPreview _preview;
        private readonly Window _toolbar;
        private bool _captureExcluded;
        private bool _closed;
        private nint _nativeHandle;
        private bool _placementFailed;

        public ScreenshotScrollingPreviewWindow(ScreenshotScrollingPreview preview, Window toolbar)
        {
            _preview = preview;
            _toolbar = toolbar;
            SystemDecorations = SystemDecorations.None;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            MinWidth = 0;
            MinHeight = 0;
            Background = AppTheme.Outline;
            _preview.ClearValue(WidthProperty);
            _preview.ClearValue(HeightProperty);
            _preview.HorizontalAlignment = HorizontalAlignment.Stretch;
            _preview.VerticalAlignment = VerticalAlignment.Stretch;
            Content = new Border
            {
                Background = Brushes.Black, BorderBrush = AppTheme.Outline,
                BorderThickness = new(BorderWidth), Child = _preview,
            };
            TransientWindowShell.Configure(this);
            if (OperatingSystem.IsWindows()) Win32Properties.AddWindowStylesCallback(this, FilterStyles);
            Closed += (_, _) =>
            {
                _closed = true;
                if (OperatingSystem.IsWindows()) Win32Properties.RemoveWindowStylesCallback(this, FilterStyles);
            };
        }

        internal PixelSize PreviewPixelSize { get; private set; }

        public void Update(Screens screens, PixelRect region, ScrollCaptureAxis axis, PixelRect toolbarBounds)
        {
            if (_closed) return;
            if (_preview.Image is null || _preview.ImageSize.Width <= 0 || _preview.ImageSize.Height <= 0 ||
                region.IsEmpty || screens.All.Count == 0)
            {
                PreviewPixelSize = default;
                Hide();
                return;
            }

            var screen = screens.All.OrderByDescending(candidate =>
            {
                var area = candidate.WorkingArea;
                var overlap = region.Intersect(new(area.X, area.Y, area.Width, area.Height));
                return (long)overlap.Width * overlap.Height;
            }).ThenByDescending(candidate =>
            {
                var area = candidate.WorkingArea;
                var overlap = toolbarBounds.Intersect(new(area.X, area.Y, area.Width, area.Height));
                return (long)overlap.Width * overlap.Height;
            }).First();
            var working = screen.WorkingArea;
            var area = new PixelRect(working.X, working.Y, working.Width, working.Height);
            var scale = screen.Scaling;
            if (!double.IsFinite(scale) || scale <= 0)
            {
                PreviewPixelSize = default;
                Hide();
                return;
            }

            var bounds = FindPlacement(area, region, axis, toolbarBounds, scale, _preview.ImageSize,
                OperatingSystem.IsWindows());
            if (bounds.IsEmpty)
            {
                PreviewPixelSize = default;
                Hide();
                return;
            }

            var border = (int)Math.Ceiling(BorderWidth * 2 * scale);
            PreviewPixelSize = new(Math.Max(1, bounds.Width - border), Math.Max(1, bounds.Height - border));
            var inside = !bounds.Intersect(region).IsEmpty;
            if (OperatingSystem.IsWindows())
            {
                var ready = PrepareNativeWindow();
                if (!ready || (inside && !_captureExcluded))
                {
                    if (!_placementFailed)
                        Logger.ZLogWarning(
                            $"Scrolling preview hidden because its native window is unavailable. Ready={ready}, InsideCapture={inside}, CaptureExcluded={_captureExcluded}, Error={Marshal.GetLastPInvokeError()}");
                    _placementFailed = true;
                    Hide();
                    return;
                }
            }

            var firstShow = !IsVisible;
            Position = new(bounds.X, bounds.Y);
            Width = bounds.Width / scale;
            Height = bounds.Height / scale;
            if (firstShow) Show();
            _preview.InvalidateVisual();
            if (OperatingSystem.IsWindows())
            {
                var toolbar = _toolbar.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle
                    ? handle.Handle
                    : 0;
                if (!SetWindowPos(_nativeHandle, toolbar != 0 ? toolbar : -1, bounds.X, bounds.Y, bounds.Width,
                        bounds.Height, 0x0210))
                {
                    if (!_placementFailed)
                        Logger.ZLogWarning(
                            $"Scrolling preview native placement failed. Error={Marshal.GetLastPInvokeError()}");
                    _placementFailed = true;
                    Hide();
                    return;
                }

                _placementFailed = false;
            }

            PreviewPixelSize = new(Math.Max(1, (int)Math.Ceiling((Width - BorderWidth * 2) * RenderScaling)),
                Math.Max(1, (int)Math.Ceiling((Height - BorderWidth * 2) * RenderScaling)));
            if (firstShow)
                Logger.ZLogInformation(
                    $"Scrolling live preview shown. Axis={axis.ToString()}, InsideCapture={inside}, CaptureExcluded={_captureExcluded}, Bounds={bounds}");
        }

        private bool PrepareNativeWindow()
        {
            if (TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle) return false;
            if (_nativeHandle == handle.Handle) return true;
            Marshal.SetLastPInvokeError(0);
            var style = GetWindowLong(handle.Handle, -20);
            if (style == 0 && Marshal.GetLastPInvokeError() != 0) return false;
            var extended = (style | 0x08000000 | 0x80 | 0x20 | 0x80000) & ~0x00040000;
            Marshal.SetLastPInvokeError(0);
            if (SetWindowLong(handle.Handle, -20, extended) == 0 && Marshal.GetLastPInvokeError() != 0)
                return false;
            if (!SetLayeredWindowAttributes(handle.Handle, 0, 255, 2)) return false;
            _nativeHandle = handle.Handle;
            _captureExcluded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                               SetWindowDisplayAffinity(handle.Handle, 0x11);
            return true;
        }

        private static (uint Style, uint ExtendedStyle) FilterStyles(uint style, uint extendedStyle)
        {
            return (style, (extendedStyle | 0x08000000 | 0x80 | 0x20 | 0x80000) & ~0x00040000u);
        }

        private static PixelRect FindPlacement(PixelRect area, PixelRect region, ScrollCaptureAxis axis,
            PixelRect toolbar, double scale, PixelSize image, bool allowInside)
        {
            var size = FitImage(area, axis, scale, image);
            if (size.Width <= 0 || size.Height <= 0) return default;
            var gap = (int)Math.Ceiling(Gap * scale);
            var sides = axis == ScrollCaptureAxis.Vertical ? new[] { 0, 1, 2, 3 } : [2, 3, 0, 1];
            foreach (var side in sides)
                if (side < 2)
                {
                    var x = side == 0 ? region.Right + gap : region.X - gap - size.Width;
                    foreach (var y in new[]
                             {
                                 region.Y, toolbar.Bottom + gap, toolbar.Y - gap - size.Height, area.Y,
                                 area.Bottom - size.Height,
                             })
                    {
                        var candidate = new PixelRect(x,
                            Math.Clamp(y, area.Y, area.Bottom - size.Height), size.Width, size.Height);
                        if (Fits(candidate, area, toolbar) && candidate.Intersect(region).IsEmpty) return candidate;
                    }
                }
                else
                {
                    var y = side == 2 ? region.Bottom + gap : region.Y - gap - size.Height;
                    foreach (var x in new[]
                             {
                                 region.X, toolbar.Right + gap, toolbar.X - gap - size.Width, area.X,
                                 area.Right - size.Width,
                             })
                    {
                        var candidate = new PixelRect(Math.Clamp(x, area.X, area.Right - size.Width), y,
                            size.Width, size.Height);
                        if (Fits(candidate, area, toolbar) && candidate.Intersect(region).IsEmpty) return candidate;
                    }
                }

            if (!allowInside) return default;
            var inside = area.Intersect(region);
            size = FitImage(inside, axis, scale, image);
            if (size.Width <= 0 || size.Height <= 0) return default;
            foreach (var point in new[]
                     {
                         new Avalonia.PixelPoint(inside.Right - size.Width, inside.Y),
                         new Avalonia.PixelPoint(inside.X, inside.Y),
                         new Avalonia.PixelPoint(inside.Right - size.Width, inside.Bottom - size.Height),
                         new Avalonia.PixelPoint(inside.X, inside.Bottom - size.Height),
                     })
            {
                var candidate = new PixelRect(point.X, point.Y, size.Width, size.Height);
                if (Fits(candidate, area, toolbar)) return candidate;
            }

            return default;
        }

        private static bool Fits(PixelRect candidate, PixelRect area, PixelRect toolbar)
        {
            return candidate.Intersect(area) == candidate && candidate.Intersect(toolbar).IsEmpty;
        }

        private static PixelSize FitImage(PixelRect area, ScrollCaptureAxis axis, double scale,
            PixelSize image)
        {
            var border = (int)Math.Ceiling(BorderWidth * scale);
            var width = area.Width - border * 2;
            var height = area.Height - border * 2;
            if (width < 1 || height < 1 || image.Width <= 0 || image.Height <= 0) return default;
            var target = Math.Max(1, TargetShortEdge * scale - border * 2);
            var ratio = Math.Min((double)width / image.Width, (double)height / image.Height);
            ratio = Math.Min(ratio, target /
                                    (axis == ScrollCaptureAxis.Vertical ? image.Width : image.Height));
            return new(Math.Max(1, (int)Math.Floor(image.Width * ratio)) + border * 2,
                Math.Max(1, (int)Math.Floor(image.Height * ratio)) + border * 2);
        }

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static partial int GetWindowLong(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static partial int SetWindowLong(nint window, int index, int value);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowDisplayAffinity(nint window, uint affinity);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height,
            uint flags);
    }
}
