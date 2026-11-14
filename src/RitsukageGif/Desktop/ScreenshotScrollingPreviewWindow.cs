using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingPreviewWindow : Window
    {
        private const double BorderWidth = 2;
        private const double Gap = 15;
        private const double TargetShortEdge = 200;
        private const double TargetLongEdge = 300;
        private const uint NoActivate = 0x08000000;
        private const uint Transparent = 0x20;
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Capture.ScrollingPreview");
        private readonly ScreenshotScrollingPreview _preview;
        private bool _captureExcluded;
        private bool _closed;
        private nint _nativeHandle;
        private PixelRect _placedBounds;
        private double _placedScale;
        private bool _placementFailed;

        public ScreenshotScrollingPreviewWindow(ScreenshotScrollingPreview preview)
        {
            _preview = preview;
            WindowDecorations = WindowDecorations.None;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            CanResize = false;
            Focusable = false;
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
                Background = Brushes.Black, Child = _preview, Padding = new(BorderWidth),
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
            _preview.UpdateView(axis);
            if (firstShow || _placedBounds != bounds || !_placedScale.Equals(scale))
            {
                Position = new(bounds.X, bounds.Y);
                Width = bounds.Width / scale;
                Height = bounds.Height / scale;
                if (firstShow) Show();
                if (OperatingSystem.IsWindows() &&
                    !SetWindowPos(_nativeHandle, -1, bounds.X, bounds.Y, bounds.Width,
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
                _placedBounds = bounds;
                _placedScale = scale;
            }

            var shortEdge = Math.Clamp((int)Math.Ceiling(TargetShortEdge * RenderScaling * 2), 1, 768);
            var longEdge = Math.Clamp(1048576 / shortEdge, 1, 4096);
            PreviewPixelSize = axis == ScrollCaptureAxis.Vertical ? new(shortEdge, longEdge) : new(longEdge, shortEdge);
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
            var extended = FilterStyles(0, (uint)style).ExtendedStyle;
            Marshal.SetLastPInvokeError(0);
            if (SetWindowLong(handle.Handle, -20, (int)extended) == 0 && Marshal.GetLastPInvokeError() != 0)
                return false;
            _nativeHandle = handle.Handle;
            _captureExcluded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                               SetWindowDisplayAffinity(handle.Handle, 0x11);
            return true;
        }

        private static (uint Style, uint ExtendedStyle) FilterStyles(uint style, uint extendedStyle)
        {
            return (style, (extendedStyle | NoActivate) & ~Transparent);
        }

        private static PixelRect FindPlacement(PixelRect area, PixelRect region, ScrollCaptureAxis axis,
            PixelRect toolbar, double scale, PixelSize image, bool allowInside)
        {
            var gap = (int)Math.Ceiling(Gap * scale);
            var sides = axis == ScrollCaptureAxis.Vertical ? new[] { 0, 1, 2, 3 } : [2, 3, 0, 1];
            foreach (var side in sides)
            {
                var available = side switch
                {
                    0 => area with { Width = area.Right - Math.Max(area.X, region.Right + gap) },
                    1 => area with { Width = Math.Min(area.Right, region.X - gap) - area.X },
                    2 => area with { Height = area.Bottom - Math.Max(area.Y, region.Bottom + gap) },
                    _ => area with { Height = Math.Min(area.Bottom, region.Y - gap) - area.Y },
                };
                var size = FitImage(available, axis, scale, image);
                if (size.Width <= 0 || size.Height <= 0) continue;
                if (side < 2)
                {
                    var x = side == 0
                        ? Math.Max(area.X, region.Right + gap)
                        : Math.Min(area.Right, region.X - gap) - size.Width;
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
                    var y = side == 2
                        ? Math.Max(area.Y, region.Bottom + gap)
                        : Math.Min(area.Bottom, region.Y - gap) - size.Height;
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
            }

            if (!allowInside) return default;
            var inside = area.Intersect(region);
            var insideSize = FitImage(inside, axis, scale, image);
            if (insideSize.Width <= 0 || insideSize.Height <= 0) return default;
            foreach (var point in new[]
                     {
                         new Avalonia.PixelPoint(inside.Right - insideSize.Width, inside.Y),
                         new Avalonia.PixelPoint(inside.X, inside.Y),
                         new Avalonia.PixelPoint(inside.Right - insideSize.Width, inside.Bottom - insideSize.Height),
                         new Avalonia.PixelPoint(inside.X, inside.Bottom - insideSize.Height),
                     })
            {
                var candidate = new PixelRect(point.X, point.Y, insideSize.Width, insideSize.Height);
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
            var vertical = axis == ScrollCaptureAxis.Vertical;
            var shortEdge = Math.Min(vertical ? area.Width : area.Height,
                (int)Math.Ceiling(TargetShortEdge * scale));
            var longEdge = Math.Min(vertical ? area.Height : area.Width, (int)Math.Ceiling(TargetLongEdge * scale));
            if (shortEdge < 64 * scale || longEdge < 64 * scale) return default;
            var aspect = vertical ? (double)image.Height / image.Width : (double)image.Width / image.Height;
            longEdge = Math.Min(longEdge, Math.Max(shortEdge / 2,
                (int)Math.Ceiling((shortEdge - border * 2) * Math.Min(aspect, 2)) + border * 2));
            return vertical ? new(shortEdge, longEdge) : new(longEdge, shortEdge);
        }

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static partial int GetWindowLong(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static partial int SetWindowLong(nint window, int index, int value);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowDisplayAffinity(nint window, uint affinity);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height,
            uint flags);
    }
}
