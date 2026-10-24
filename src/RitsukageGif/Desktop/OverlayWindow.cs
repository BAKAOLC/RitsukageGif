using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class OverlayWindow : Window
    {
        private readonly OverlayCanvas _canvas;
        private readonly SelectionCoordinator _coordinator;
        private readonly bool _sourceLocal;
        private IPointer? _captured;
        private bool _imageDisposed;

        public OverlayWindow(SelectionCoordinator coordinator, PixelRect region, double scaling, byte[] pixels,
            int radius, int threshold, bool sourceLocal = false)
        {
            _coordinator = coordinator;
            _sourceLocal = sourceLocal;
            Region = region;
            this.Bind(TitleProperty, LiveText.Resource("SelectionTitle"));
            SystemDecorations = sourceLocal ? SystemDecorations.Full : SystemDecorations.None;
            CanResize = false;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = sourceLocal ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual;
            Position = new Avalonia.PixelPoint(region.X, region.Y);
            Width = region.Width / scaling;
            Height = region.Height / scaling;
            Background = Brushes.Black;
            Cursor = new Cursor(StandardCursorType.Cross);
            _canvas = new OverlayCanvas(region, pixels, radius, threshold)
            {
                Scale = scaling, ShowControls = coordinator.CanDetectControls,
                ShowSource = coordinator.CanSwitchSource, Controls = coordinator.Controls,
                WindowSource = coordinator.WindowSource
            };
            Content = _canvas;
            UiText.LanguageChanged += _canvas.InvalidateVisual;
            Closed += (_, _) => UiText.LanguageChanged -= _canvas.InvalidateVisual;
            Opened += (_, _) =>
            {
                if (!sourceLocal && OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
                    NativeMethods.SetWindowPos(handle.Handle, -1, region.X, region.Y, region.Width, region.Height,
                        0x0040);
            };

            PixelPoint SourcePoint(PointerEventArgs args)
            {
                if (!sourceLocal)
                {
                    var point = this.PointToScreen(args.GetPosition(this));
                    return new PixelPoint(point.X, point.Y);
                }

                var local = args.GetPosition(_canvas);
                return new PixelPoint(
                    region.X + (int)Math.Clamp(Math.Round(local.X * region.Width / Math.Max(1, _canvas.Bounds.Width)),
                        0, region.Width),
                    region.Y + (int)Math.Clamp(Math.Round(local.Y * region.Height / Math.Max(1, _canvas.Bounds.Height)),
                        0, region.Height));
            }

            void Pointer(PointerEventArgs args, bool pressed = false)
            {
                var properties = args.GetCurrentPoint(this).Properties;
                coordinator.PointerInput(SourcePoint(args), properties.IsLeftButtonPressed,
                    properties.IsRightButtonPressed, args.KeyModifiers.HasFlag(KeyModifiers.Shift), pressed);
                args.Handled = true;
            }

            _canvas.PointerEntered += (_, args) => Pointer(args);
            _canvas.PointerMoved += (_, args) => Pointer(args);
            _canvas.PointerPressed += (_, args) =>
            {
                var properties = args.GetCurrentPoint(this).Properties;
                if (properties.IsLeftButtonPressed || properties.IsRightButtonPressed)
                {
                    _captured = args.Pointer;
                    args.Pointer.Capture(_canvas);
                }

                Pointer(args, true);
            };
            _canvas.PointerReleased += (_, args) =>
            {
                Pointer(args);
                var properties = args.GetCurrentPoint(this).Properties;
                if (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed) ReleaseCapture();
            };
            PointerCaptureLost += (_, _) =>
            {
                _captured = null;
                coordinator.CaptureLost();
            };
            KeyDown += (_, args) =>
            {
                coordinator.KeyInput(args.Key, true, args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                args.Handled =
                    args.Key is Key.Enter or Key.Escape or Key.Delete or Key.Back or Key.LeftShift or Key.RightShift
                        or Key.Tab or Key.W;
            };
            KeyUp += (_, args) =>
            {
                coordinator.KeyInput(args.Key, false, args.KeyModifiers.HasFlag(KeyModifiers.Shift));
                args.Handled =
                    args.Key is Key.Enter or Key.Escape or Key.Delete or Key.Back or Key.LeftShift or Key.RightShift
                        or Key.Tab or Key.W;
            };
            Deactivated += (_, _) => coordinator.FocusChanged();
            Closed += (_, _) => coordinator.Cancel();
        }

        public PixelRect Region { get; }

        public PixelPoint Snap(PixelPoint point, int mode) => _canvas.Snap(point, mode);

        public void Refresh(PixelRect selected, PixelPoint point, int mode)
        {
            _canvas.Selected = selected;
            _canvas.Mouse = point;
            _canvas.Mode = mode;
            if (_sourceLocal && _canvas.Bounds.Width > 0) _canvas.Scale = Region.Width / _canvas.Bounds.Width;
            _canvas.Controls = _coordinator.Controls;
            _canvas.WindowSource = _coordinator.WindowSource;
            _canvas.InvalidateVisual();
        }

        public void ReleaseCapture()
        {
            var pointer = _captured;
            _captured = null;
            pointer?.Capture(null);
        }

        public void DisposeImage()
        {
            if (_imageDisposed) return;
            _imageDisposed = true;
            _canvas.Dispose();
        }
    }
}
