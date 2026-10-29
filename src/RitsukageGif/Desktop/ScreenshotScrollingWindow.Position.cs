using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        private ScreenshotScrollingPreviewWindow? _imagePreviewWindow;

        private bool _manualToolbarPosition;

        private void MoveRegion(int position)
        {
            var candidate = _axis == ScrollCaptureAxis.Vertical
                ? _region with { Y = position }
                : _region with { X = position };
            var monitors = _screens.All.Select(screen => new PixelRect(screen.Bounds.X, screen.Bounds.Y,
                screen.Bounds.Width, screen.Bounds.Height));
            if (!new DisplayTopology(monitors).Covers(candidate)) return;
            _region = candidate;
            _session.Move(position);
            UpdateBorders();
            UpdateObstruction();
            RefreshLivePreview();
        }

        private void UpdateBorders()
        {
            if (OperatingSystem.IsWindows())
            {
                UpdateMasks();
                return;
            }

            var rectangles = _screens.All.SelectMany(screen => CreateBorderRectangles(_region,
                [new(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height)],
                (int)Math.Ceiling(_appearance.SelectionBorderWidth * screen.Scaling))).ToList();
            for (var i = 0; i < rectangles.Count; i++)
            {
                var rectangle = rectangles[i];
                var screen = _screens.ScreenFromPoint(new(rectangle.X, rectangle.Y));
                var scale = screen?.Scaling ?? RenderScaling;
                Window border;
                if (_borders.Count <= i)
                {
                    border = new()
                    {
                        SystemDecorations = SystemDecorations.None, ShowInTaskbar = false, ShowActivated = false,
                        Topmost = true, CanResize = false, Background = Brushes.Transparent,
                        TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                        Focusable = false, IsHitTestVisible = false,
                        WindowStartupLocation = WindowStartupLocation.Manual, MinWidth = 0, MinHeight = 0,
                    };
                    TransientWindowShell.Configure(border);
                    _borders.Add(border);
                }
                else
                {
                    border = _borders[i];
                }

                border.Position = new(rectangle.X, rectangle.Y);
                border.Width = rectangle.Width / scale;
                border.Content = new Border
                {
                    Width = rectangle.Width / scale, Height = rectangle.Height / scale,
                    Background =
                        new SolidColorBrush(
                            Color.FromUInt32(_appearance.SelectionBorderColor ?? _appearance.AccentColor)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                border.Height = rectangle.Height / scale;
                if (!border.IsVisible) border.Show();
                var excluded = Exclude(border);
                if (!_windowSource && !excluded && !rectangle.Intersect(_region).IsEmpty) border.Hide();

                if (OperatingSystem.IsWindows() && border.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle)
                {
                    SetBorderInputTransparent(handle.Handle);
                    _ = NativeMethods.SetWindowPos(handle.Handle, -1, rectangle.X, rectangle.Y, rectangle.Width,
                        rectangle.Height, 0x0010);
                    ClipBorder(handle.Handle, rectangle.Width, rectangle.Height);
                }
            }

            for (var index = _borders.Count - 1; index >= rectangles.Count; index--)
            {
                _borders[index].Close();
                _borders.RemoveAt(index);
            }
        }

        private void UpdateMasks()
        {
            var toolbar = TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle ? handle.Handle : 0;
            for (var index = 0; index < _screens.All.Count; index++)
            {
                var screen = _screens.All[index];
                if (_maskWindows.Count <= index) _maskWindows.Add(new(_appearance));
                _maskWindows[index].Update(new(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width,
                    screen.Bounds.Height), _region, screen.Scaling, toolbar);
            }

            for (var index = _maskWindows.Count - 1; index >= _screens.All.Count; index--)
            {
                _maskWindows[index].Close();
                _maskWindows.RemoveAt(index);
            }
        }

        private void ToolbarScreensChanged(object? sender, EventArgs args)
        {
            if (IsVisible) Reposition();
        }

        private void Reposition()
        {
            var layoutWidth = Bounds.Width > 0 ? Bounds.Width : 600;
            var layoutHeight = Bounds.Height > 0 ? Bounds.Height : 70;
            var screens = _screens.All.Select(screen =>
                (new PixelRect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width,
                    screen.WorkingArea.Height), screen.Scaling)).ToArray();
            var position = _manualToolbarPosition
                ? ClampManualToolbarPosition(layoutWidth, layoutHeight, screens)
                : FindPanelPosition(_region, layoutWidth, layoutHeight, screens, _preferredPlacementScreen);
            if (Position != position) Position = position;
            UpdateObstruction();
            RefreshLivePreview();
        }

        private Avalonia.PixelPoint ClampManualToolbarPosition(double layoutWidth, double layoutHeight,
            IReadOnlyList<(PixelRect Area, double Scale)> screens)
        {
            var current = new PixelRect(Position.X, Position.Y, (int)Math.Ceiling(layoutWidth * RenderScaling),
                (int)Math.Ceiling(layoutHeight * RenderScaling));
            var (area, scale) = FindPanelScreen(current, screens);
            var width = (int)Math.Ceiling(layoutWidth * scale);
            var height = (int)Math.Ceiling(layoutHeight * scale);
            return new(Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - width)),
                Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
        }

        private static (PixelRect Area, double Scale) FindPanelScreen(PixelRect region,
            IReadOnlyList<(PixelRect Area, double Scale)> screens, PixelRect? preferredScreen = null)
        {
            if (preferredScreen is { } preferred && !preferred.Intersect(region).IsEmpty)
                foreach (var screen in screens)
                    if (!screen.Area.IsEmpty && double.IsFinite(screen.Scale) && screen.Scale > 0 &&
                        !screen.Area.Intersect(region).IsEmpty &&
                        !screen.Area.Intersect(preferred).IsEmpty)
                        return screen;
            var best = (Area: new PixelRect(0, 0, 1280, 800), Scale: 1d);
            var bestOverlap = -1L;
            var bestDistance = long.MaxValue;
            foreach (var screen in screens)
            {
                if (screen.Area.IsEmpty || !double.IsFinite(screen.Scale) || screen.Scale <= 0) continue;
                var overlap = screen.Area.Intersect(region);
                var area = (long)overlap.Width * overlap.Height;
                var distance = Math.Abs((long)screen.Area.X + screen.Area.Width / 2 - region.X - region.Width / 2) +
                               Math.Abs((long)screen.Area.Y + screen.Area.Height / 2 - region.Y - region.Height / 2);
                if (area < bestOverlap || (area == bestOverlap && distance >= bestDistance)) continue;
                best = screen;
                bestOverlap = area;
                bestDistance = distance;
            }

            return best;
        }

        private static Avalonia.PixelPoint FindPanelPosition(PixelRect region, double layoutWidth,
            double layoutHeight, IReadOnlyList<(PixelRect Area, double Scale)> screens,
            PixelRect? preferredScreen = null)
        {
            var (area, scale) = FindPanelScreen(region, screens, preferredScreen);
            var width = (int)Math.Ceiling(layoutWidth * scale);
            var height = (int)Math.Ceiling(layoutHeight * scale);
            var gap = (int)Math.Ceiling(6 * scale);
            var x = Math.Clamp(region.Right - width, area.X, Math.Max(area.X, area.Right - width));
            var below = region.Bottom + gap;
            if (below >= area.Y && (long)below + height <= area.Bottom) return new(x, below);
            var above = region.Y - height - gap;
            if (above >= area.Y && (long)above + height <= area.Bottom) return new(x, above);
            return new(x, Math.Clamp(below, area.Y, Math.Max(area.Y, area.Bottom - height)));
        }

        private void UpdateObstruction()
        {
            if (_closed) return;
            var bounds = new PixelRect(Position.X, Position.Y, (int)Math.Ceiling(Bounds.Width * RenderScaling),
                (int)Math.Ceiling(Bounds.Height * RenderScaling));
            var obstructed = !_windowSource && !_excluded && !bounds.Intersect(_region).IsEmpty;
            if (!_windowSource && !_previewExcluded &&
                _cropPopup is { IsOpen: true, Child: { } child } && GetTopLevel(child) is { } root)
            {
                var position = child.PointToScreen(default);
                var popupBounds = new PixelRect(position.X, position.Y,
                    (int)Math.Ceiling(child.Bounds.Width * root.RenderScaling),
                    (int)Math.Ceiling(child.Bounds.Height * root.RenderScaling));
                obstructed |= !popupBounds.Intersect(_region).IsEmpty;
            }

            _session.Obstructed = obstructed;
            _obstructed = obstructed;
            RefreshStatus();
        }

        private void RefreshLivePreview()
        {
            if (_closed || _session.Paused || _outputting || _preview.Image is null)
            {
                _imagePreviewWindow?.Hide();
                return;
            }

            _imagePreviewWindow ??= new(_preview, this);
            var bounds = new PixelRect(Position.X, Position.Y, (int)Math.Ceiling(Bounds.Width * RenderScaling),
                (int)Math.Ceiling(Bounds.Height * RenderScaling));
            _imagePreviewWindow.Update(_screens, _region, _axis, bounds);
            if (_imagePreviewWindow.IsVisible)
                _session.ConfigurePreviewSize(_imagePreviewWindow.PreviewPixelSize.Width,
                    _imagePreviewWindow.PreviewPixelSize.Height);
        }

        private void CloseLivePreview()
        {
            _cropCloseTimer?.Dispose();
            _cropCloseTimer = null;
            _cropPopup.IsOpen = false;
            _imagePreviewWindow?.Close();
            _imagePreviewWindow = null;
        }

        private static bool Exclude(TopLevel window)
        {
            return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                   window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle &&
                   SetWindowDisplayAffinity(handle.Handle, 0x11);
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowDisplayAffinity(nint window, uint affinity);
    }
}
