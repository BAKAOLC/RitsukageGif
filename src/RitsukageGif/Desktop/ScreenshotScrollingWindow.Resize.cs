using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private const int ResizeLeft = 1;
        private const int ResizeTop = 2;
        private const int ResizeRight = 4;
        private const int ResizeBottom = 8;
        private static readonly Cursor ResizeHorizontalCursor = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor ResizeVerticalCursor = new(StandardCursorType.SizeNorthSouth);
        private static readonly Cursor ResizeDescendingCursor = new(StandardCursorType.TopLeftCorner);
        private static readonly Cursor ResizeAscendingCursor = new(StandardCursorType.TopRightCorner);
        private readonly List<Window> _resizeTargets = [];
        private IPointer? _resizePointer;
        private PixelPoint _resizeAnchor;
        private PixelRect? _resizePreview;
        private int _resizeEdges;

        private bool CanResizeRegion => !_closed && !_captureStarted && !_outputting &&
                                        !HasCaptureDialog && !DesktopCapture.UsesSourceSelection;

        private void UpdateResizeTargets()
        {
            if (!CanResizeRegion)
            {
                foreach (var target in _resizeTargets) target.Hide();
                return;
            }

            // A captured pointer stays on its original target until release.
            if (_resizePointer is not null) return;
            var count = 0;
            foreach (var screen in _screens.All)
            {
                var monitor = new PixelRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width,
                    screen.Bounds.Height);
                if (_region.Intersect(monitor).IsEmpty) continue;
                var margin = Math.Max(1, (int)Math.Round(4 * screen.Scaling));
                foreach (var strip in ResizeTargetRects(_region, monitor, margin))
                {
                    var target = _resizeTargets.Count > count ? _resizeTargets[count] : NewResizeTarget();
                    count++;
                    target.Tag = (monitor, margin);
                    target.Position = new(strip.X, strip.Y);
                    target.Width = strip.Width / screen.Scaling;
                    target.Height = strip.Height / screen.Scaling;
                    if (!target.IsVisible) target.Show(this);
                    if (OperatingSystem.IsWindows() &&
                        target.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle)
                    {
                        const uint moveWithoutActivation = 0x0214;
                        _ = NativeMethods.SetWindowPos(handle.Handle, 0, strip.X, strip.Y, strip.Width, strip.Height,
                            moveWithoutActivation);
                        // Win32 may enlarge a narrow top-level window to the minimum track size.
                        ClipBorder(handle.Handle, strip.Width, strip.Height);
                    }
                }
            }

            for (var index = count; index < _resizeTargets.Count; index++) _resizeTargets[index].Hide();
        }

        private Window NewResizeTarget()
        {
            var target = new Window
            {
                WindowDecorations = WindowDecorations.None,
                ShowActivated = false,
                Topmost = true,
                CanResize = false,
                Focusable = false,
                Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                WindowStartupLocation = WindowStartupLocation.Manual,
                MinWidth = 0,
                MinHeight = 0,
            };
            TransientWindowShell.Configure(target);
            target.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (!CanResizeRegion || _resizePointer is not null || _dragStart is not null ||
                    !args.GetCurrentPoint(target).Properties.IsLeftButtonPressed) return;
                var point = RegionPointer(target, args);
                var edges = ResizeTargetEdges(target, point);
                if (edges == 0) return;
                args.Pointer.Capture(target);
                if (!ReferenceEquals(args.Pointer.Captured, target)) return;
                _resizeAnchor = point;
                _resizeEdges = edges;
                _resizePointer = args.Pointer;
                args.Handled = true;
            }, RoutingStrategies.Tunnel);
            target.AddHandler(PointerMovedEvent, (_, args) =>
            {
                var point = RegionPointer(target, args);
                if (!ReferenceEquals(args.Pointer, _resizePointer))
                {
                    target.Cursor = ResizeCursor(ResizeTargetEdges(target, point));
                    return;
                }

                if (CanResizeRegion)
                {
                    var candidate = GetResizedRegion(_region, _resizeEdges, point.X - _resizeAnchor.X,
                        point.Y - _resizeAnchor.Y);
                    if (candidate != (_resizePreview ?? _region) && _session.SupportsStoppedResize(candidate) &&
                        IsAllowedRegion(candidate, true))
                    {
                        _resizePreview = candidate;
                        SetResizeSize(candidate);
                        UpdateBorders();
                    }
                }

                args.Handled = true;
            }, RoutingStrategies.Tunnel);
            target.AddHandler(PointerReleasedEvent, (_, args) =>
            {
                if (!ReferenceEquals(args.Pointer, _resizePointer)) return;
                args.Handled = true;
                FinishResize(CanResizeRegion);
            }, RoutingStrategies.Tunnel);
            target.PointerCaptureLost += (_, args) =>
            {
                if (ReferenceEquals(args.Pointer, _resizePointer)) FinishResize(false);
            };
            _resizeTargets.Add(target);
            return target;
        }

        private static PixelPoint RegionPointer(Window target, PointerEventArgs args)
        {
            var point = target.PointToScreen(args.GetPosition(target));
            return new(point.X, point.Y);
        }

        private void FinishResize(bool accept)
        {
            if (_resizePointer is not { } pointer) return;
            var candidate = _resizePreview;
            _resizePointer = null;
            _resizePreview = null;
            pointer.Capture(null);
            if (_closed) return;
            if (accept && candidate is { } resized && resized != _region)
            {
                _session.ConfigureAxis(_axis, resized);
                _session.ConfigureInsets(0, 0);
                _region = resized;
                Reposition();
            }

            SetResizeSize(_region);
            UpdateBorders();
        }

        private void CloseResizeTargets()
        {
            FinishResize(false);
            foreach (var target in _resizeTargets) target.Close();
            _resizeTargets.Clear();
        }

        private void SetResizeSize(PixelRect region)
        {
            _size.Text = $"{region.Width} × {region.Height}";
        }

        private static IEnumerable<PixelRect> ResizeTargetRects(PixelRect region, PixelRect monitor, int margin)
        {
            var x = region.X;
            var y = region.Y;
            var right = region.Right;
            var bottom = region.Bottom;
            var outerLeft = x - margin;
            var outerTop = y - margin;
            var outerRight = right + margin;
            var outerBottom = bottom + margin;
            var leftWidth = x == monitor.X ? margin * 2 : margin;
            var topHeight = y == monitor.Y ? margin * 2 : margin;
            var rightWidth = right == monitor.Right ? margin * 2 : margin;
            var bottomHeight = bottom == monitor.Bottom ? margin * 2 : margin;
            var top = new PixelRect(outerLeft, outerTop, outerRight - outerLeft, topHeight);
            var lower = new PixelRect(outerLeft, outerBottom - bottomHeight, outerRight - outerLeft, bottomHeight);
            var sideY = top.Bottom;
            var sideHeight = lower.Y - sideY;
            foreach (var strip in new[]
                     {
                         top, lower, new(outerLeft, sideY, leftWidth, sideHeight),
                         new(outerRight - rightWidth, sideY, rightWidth, sideHeight),
                     })
            {
                var clipped = strip.Intersect(monitor);
                if (!clipped.IsEmpty) yield return clipped;
            }
        }

        private int ResizeTargetEdges(Window target, PixelPoint point)
        {
            return target.Tag is ValueTuple<PixelRect, int> band
                ? ResizeEdgesAt(_region, band.Item1, band.Item2, point)
                : 0;
        }

        private static int ResizeEdgesAt(PixelRect region, PixelRect monitor, int margin, PixelPoint point)
        {
            var left = point.X < region.X || (region.X == monitor.X && point.X < region.X + margin);
            var right = point.X >= region.Right || (region.Right == monitor.Right && point.X >= region.Right - margin);
            var top = point.Y < region.Y || (region.Y == monitor.Y && point.Y < region.Y + margin);
            var bottom = point.Y >= region.Bottom ||
                         (region.Bottom == monitor.Bottom && point.Y >= region.Bottom - margin);
            return (left ? ResizeLeft : right ? ResizeRight : 0) | (top ? ResizeTop : bottom ? ResizeBottom : 0);
        }

        private static Cursor ResizeCursor(int edges)
        {
            return edges switch
            {
                ResizeLeft or ResizeRight => ResizeHorizontalCursor,
                ResizeTop or ResizeBottom => ResizeVerticalCursor,
                (ResizeLeft | ResizeTop) or (ResizeRight | ResizeBottom) => ResizeDescendingCursor,
                _ => ResizeAscendingCursor,
            };
        }

        private static PixelRect GetResizedRegion(PixelRect origin, int edges, int dx, int dy)
        {
            var left = origin.X + ((edges & ResizeLeft) != 0 ? dx : 0);
            var right = origin.Right + ((edges & ResizeRight) != 0 ? dx : 0);
            var top = origin.Y + ((edges & ResizeTop) != 0 ? dy : 0);
            var bottom = origin.Bottom + ((edges & ResizeBottom) != 0 ? dy : 0);
            return new(Math.Min(left, right), Math.Min(top, bottom), Math.Abs(right - left), Math.Abs(bottom - top));
        }
    }
}
