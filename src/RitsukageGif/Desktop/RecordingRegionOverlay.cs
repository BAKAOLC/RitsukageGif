using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingRegionOverlay : IDisposable
    {
        private readonly Action<PixelRect> _changed;
        private readonly List<Window> _borders = [];
        private readonly List<nint> _inputStyles = [];
        private readonly List<bool> _through = [];
        private readonly Cursor _horizontalCursor = new(StandardCursorType.SizeWestEast);
        private readonly Cursor _verticalCursor = new(StandardCursorType.SizeNorthSouth);
        private readonly Cursor _diagonalCursor = new(StandardCursorType.TopLeftCorner);
        private readonly Cursor _otherDiagonalCursor = new(StandardCursorType.TopRightCorner);
        private PixelRect _region;
        private IBrush _brush = AppTheme.Accent;
        private PixelRect _dragRegion;
        private Avalonia.PixelPoint? _dragOrigin;
        private int _dragEdge;
        private bool _editable;
        private bool _disposed;
        private bool _layoutQueued;

        internal RecordingRegionOverlay(Action<PixelRect> changed)
        {
            _changed = changed;
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var border in _borders) border.Close();
            _borders.Clear();
            _inputStyles.Clear();
            _through.Clear();
            _horizontalCursor.Dispose();
            _verticalCursor.Dispose();
            _diagonalCursor.Dispose();
            _otherDiagonalCursor.Dispose();
        }

        internal event Action? LayoutChanged;

        internal void SetBrush(IBrush brush)
        {
            if (ReferenceEquals(_brush, brush)) return;
            _brush = brush;
            foreach (var border in _borders) ((Border)border.Content!).BorderBrush = brush;
        }

        internal void Update(Window owner, PixelRect region, bool editable, bool visible)
        {
            _region = region;
            _editable = editable;
            if (!visible || DesktopCapture.UsesSourceSelection)
            {
                foreach (var border in _borders) border.Hide();
                return;
            }

            var index = 0;
            for (var edge = 0; edge < 4; edge++)
                foreach (var screen in owner.Screens.All)
                {
                    var hit = (int)Math.Ceiling(6 * screen.Scaling);
                    var segment = edge switch
                    {
                        0 => new PixelRect(region.X - hit, region.Y - hit, region.Width + hit * 2, hit),
                        1 => new PixelRect(region.Right, region.Y, hit, region.Height),
                        2 => new PixelRect(region.X - hit, region.Bottom, region.Width + hit * 2, hit),
                        _ => new PixelRect(region.X - hit, region.Y, hit, region.Height),
                    };
                    var clipped = segment.Intersect(new(screen.Bounds.X, screen.Bounds.Y,
                        screen.Bounds.Width, screen.Bounds.Height));
                    if (clipped.IsEmpty) continue;
                    if (index == _borders.Count) AddBorder();
                    var border = _borders[index];
                    border.Tag = edge;
                    border.Position = new(clipped.X, clipped.Y);
                    border.Width = clipped.Width / screen.Scaling;
                    border.Height = clipped.Height / screen.Scaling;
                    ((Border)border.Content!).BorderThickness = edge switch
                    {
                        0 => new(0, 0, 0, 1), 1 => new(1, 0, 0, 0), 2 => new(0, 1, 0, 0), _ => new(0, 0, 1, 0),
                    };
                    border.IsHitTestVisible = editable;
                    border.Cursor = edge % 2 == 0 ? _verticalCursor : _horizontalCursor;
                    if (!border.IsVisible) border.Show();
                    Exclude(border);
                    if (_through[index] != !editable && PinnedWindowPlatform.CanSetMouseThrough(border))
                    {
                        var style = _inputStyles[index];
                        PinnedWindowPlatform.SetMouseThrough(border, !editable, ref style);
                        _inputStyles[index] = style;
                        _through[index] = !editable;
                    }

                    index++;
                }

            for (; index < _borders.Count; index++) _borders[index].Hide();
        }

        private void AddBorder()
        {
            var border = new Window
            {
                WindowDecorations = WindowDecorations.None, ShowActivated = false, Topmost = true,
                CanResize = false, MinWidth = 0, MinHeight = 0,
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                Content = new Border { BorderBrush = _brush },
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            TransientWindowShell.Configure(border);
            border.ScalingChanged += (_, _) => ScheduleLayout();
            border.PointerPressed += (_, args) =>
            {
                if (!_editable || !args.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
                _dragOrigin = border.PointToScreen(args.GetPosition(border));
                _dragRegion = _region;
                _dragEdge = HitEdges(border, _dragOrigin.Value);
                args.Pointer.Capture(border);
                args.Handled = true;
            };
            border.PointerMoved += (_, args) =>
            {
                var point = border.PointToScreen(args.GetPosition(border));
                if (_dragOrigin is not { } origin)
                {
                    var edges = HitEdges(border, point);
                    border.Cursor = edges switch
                    {
                        3 or 12 => _diagonalCursor, 6 or 9 => _otherDiagonalCursor,
                        1 or 4 => _horizontalCursor, _ => _verticalCursor,
                    };
                    return;
                }

                var dx = point.X - origin.X;
                var dy = point.Y - origin.Y;
                var left = (_dragEdge & 1) != 0 ? Math.Min(_dragRegion.Right - 1, _dragRegion.X + dx) : _dragRegion.X;
                var top = (_dragEdge & 2) != 0 ? Math.Min(_dragRegion.Bottom - 1, _dragRegion.Y + dy) : _dragRegion.Y;
                var right = (_dragEdge & 4) != 0
                    ? Math.Max(_dragRegion.X + 1, _dragRegion.Right + dx)
                    : _dragRegion.Right;
                var bottom = (_dragEdge & 8) != 0
                    ? Math.Max(_dragRegion.Y + 1, _dragRegion.Bottom + dy)
                    : _dragRegion.Bottom;
                var next = new PixelRect(left, top, right - left, bottom - top);
                _changed(next);
                args.Handled = true;
            };
            border.PointerReleased += (_, args) =>
            {
                _dragOrigin = null;
                args.Pointer.Capture(null);
            };
            border.PointerCaptureLost += (_, _) => _dragOrigin = null;
            _borders.Add(border);
            _inputStyles.Add(0);
            _through.Add(false);
        }

        private int HitEdges(Window border, Avalonia.PixelPoint point)
        {
            var edge = (int)border.Tag!;
            var mask = edge switch { 0 => 2, 1 => 4, 2 => 8, _ => 1 };
            var margin = 8 * border.RenderScaling;
            return mask | (edge % 2 == 0
                ? point.X < _region.X + margin ? 1 : point.X > _region.Right - margin ? 4 : 0
                : point.Y < _region.Y + margin
                    ? 2
                    : point.Y > _region.Bottom - margin
                        ? 8
                        : 0);
        }

        private void ScheduleLayout()
        {
            if (_disposed || _layoutQueued) return;
            _layoutQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _layoutQueued = false;
                if (!_disposed) LayoutChanged?.Invoke();
            });
        }

        internal static bool Exclude(Window window, bool excluded = true)
        {
            return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                   window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle &&
                   SetWindowDisplayAffinity(handle.Handle, excluded ? 0x11u : 0);
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowDisplayAffinity(nint window, uint affinity);
    }
}
