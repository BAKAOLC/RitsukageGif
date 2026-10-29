using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayWindow : Window
    {
        private static readonly Dictionary<StandardCursorType, Cursor> SelectionCursors =
            new()
            {
                [StandardCursorType.Cross] = new(StandardCursorType.Cross),
                [StandardCursorType.SizeAll] = new(StandardCursorType.SizeAll),
                [StandardCursorType.SizeWestEast] = new(StandardCursorType.SizeWestEast),
                [StandardCursorType.SizeNorthSouth] = new(StandardCursorType.SizeNorthSouth),
                [StandardCursorType.TopLeftCorner] = new(StandardCursorType.TopLeftCorner),
                [StandardCursorType.TopRightCorner] = new(StandardCursorType.TopRightCorner),
            };

        private readonly ScreenshotAnnotationSession? _annotationSession;
        private readonly ScreenshotAnnotationSurface? _annotationSurface;
        private readonly OverlayCanvas _canvas;
        private readonly SelectionCoordinator _coordinator;
        private readonly SelectionFrameControls _frameControls;
        private readonly Grid _layers;
        private readonly ScreenshotToolbar? _screenshotToolbar;
        private readonly SelectionActionBar? _selectionActions;
        private readonly SelectionOptionsBar _selectionOptions;
        private readonly bool _sourceLocal;
        private IPointer? _captured;
        private ScreenshotCursorOverlay? _cursorOverlay;
        private bool _imageDisposed;
        private int _lastMode;
        private PixelPoint _lastPoint;
        private byte[]? _screenPixels;
        private FrozenWindowScreenshot? _shownWindowFrame;

        public OverlayWindow(SelectionCoordinator coordinator, PixelRect region, double scaling, byte[] pixels,
            int radius, int threshold, bool sourceLocal = false)
        {
            _coordinator = coordinator;
            _sourceLocal = sourceLocal;
            Region = region;
            Bind(TitleProperty, LiveText.Resource("SelectionTitle"));
            SystemDecorations = sourceLocal ? SystemDecorations.Full : SystemDecorations.None;
            CanResize = false;
            TransientWindowShell.Configure(this);
            Topmost = true;
            WindowStartupLocation = sourceLocal ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual;
            Position = new(region.X, region.Y);
            Width = region.Width / scaling;
            Height = region.Height / scaling;
            Background = Brushes.Black;
            Cursor = SelectionCursors[StandardCursorType.Cross];
            _canvas = new(region, pixels, radius, threshold, coordinator.ScreenshotPreview)
            {
                ShortcutBindings = coordinator.ShortcutBindings,
                Scale = scaling, ShowControls = coordinator.CanDetectControls,
                ShowSource = coordinator.CanSwitchSource, Controls = coordinator.Controls,
                ShowCursor = coordinator.CanToggleCursor,
                WindowSource = coordinator.WindowSource,
            };
            var layers = new Grid();
            _layers = layers;
            _screenPixels = coordinator.ScreenshotMode ? pixels : null;
            layers.Children.Add(_canvas);
            if (!sourceLocal && coordinator.ScreenshotCursor is { } frozenCursor)
            {
                _cursorOverlay = new(frozenCursor, region, pixels, scaling);
                layers.Children.Add(_cursorOverlay);
            }

            Content = layers;
            UiText.LanguageChanged += _canvas.InvalidateVisual;
            Closed += (_, _) => UiText.LanguageChanged -= _canvas.InvalidateVisual;
            Opened += (_, _) =>
            {
                if (!sourceLocal && OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle &&
                    NativeMethods.SetWindowPos(handle.Handle, -1, region.X, region.Y, region.Width, region.Height,
                        0x0040) == 0)
                    // ReSharper disable once InvocationIsSkipped
                    Debug.WriteLine("Physical overlay positioning failed; keeping platform layout.");
            };

            if (coordinator.Annotations is { } annotations)
            {
                _annotationSession = annotations;
                annotations.RegisterFrame(region, pixels);
                _annotationSurface = new(annotations, region, scaling, SourcePoint, coordinator);
                _screenshotToolbar = new(annotations, region, scaling, coordinator.ShortcutBindings,
                    coordinator.ExecuteToolbarCommand, dialogScopeProvider: coordinator.BeginSelectionDialog);
                layers.Children.Add(_annotationSurface);
                layers.Children.Add(_screenshotToolbar);
                annotations.Changed += AnnotationChanged;
                _screenshotToolbar.Refresh(default, default, false);
            }

            if (coordinator.ScreenshotPreview) ConfigureScrollingPreview(layers, region, scaling);

            if (!coordinator.ScreenshotMode)
            {
                _selectionActions = new(coordinator.ExecuteToolbarCommand, coordinator.ShortcutBindings)
                    { IsVisible = false };
                layers.Children.Add(_selectionActions);
            }

            _selectionOptions = new(coordinator.ExecuteToolbarCommand, coordinator.ShortcutBindings)
                { IsVisible = false };
            _selectionOptions.AttachGeometry(coordinator);
            layers.Children.Add(_selectionOptions);
            _frameControls = new(coordinator) { IsVisible = false };
            _frameControls.LayoutChanged += () =>
            {
                _canvas.FrameReservedBounds = _frameControls.OccupiedBounds;
                _canvas.InvalidateVisual();
            };
            layers.Children.Add(new ThemeVariantScope
            {
                Child = _frameControls,
            });
            AddHandler(PointerPressedEvent, (_, _) =>
            {
                if (!_frameControls.IsPointerOver) _frameControls.ClosePopup();
            }, RoutingStrategies.Tunnel, true);

            InitializeHdrBackground();
            ConfigureMouseInput(SourcePoint);
            AddHandler(PointerMovedEvent, (_, args) => coordinator.UpdateAnnotationPointer(SourcePoint(args)),
                RoutingStrategies.Tunnel, true);
            PointerCaptureLost += (_, _) =>
            {
                _captured = null;
                ResetMouseInput();
                coordinator.CaptureLost();
            };
            AddHandler(KeyDownEvent, (_, args) =>
            {
                if (coordinator.HandleTextInputKey(args.Key, true, FocusManager?.GetFocusedElement() is TextBox))
                    return;

                coordinator.KeyInput(args.Key, true, args.KeyModifiers);
                if (SelectionCommands.IsCommandKey(args.Key, args.KeyModifiers, coordinator.ShortcutBindings))
                    args.Handled = true;
            }, RoutingStrategies.Tunnel, true);
            AddHandler(KeyUpEvent, (_, args) =>
            {
                if (coordinator.HandleTextInputKey(args.Key, false, FocusManager?.GetFocusedElement() is TextBox))
                    return;

                coordinator.KeyInput(args.Key, false, args.KeyModifiers);
                if (SelectionCommands.IsCommandKey(args.Key, args.KeyModifiers, coordinator.ShortcutBindings))
                    args.Handled = true;
            }, RoutingStrategies.Tunnel, true);
            Deactivated += (_, _) => coordinator.FocusChanged();
            Closed += (_, _) => coordinator.OverlayClosed();
            return;

            PixelPoint SourcePoint(PointerEventArgs args)
            {
                if (!sourceLocal)
                {
                    var point = this.PointToScreen(args.GetPosition(this));
                    return new(point.X, point.Y);
                }

                var local = args.GetPosition(_canvas);
                return new(
                    region.X + (int)Math.Clamp(Math.Round(local.X * region.Width / Math.Max(1, _canvas.Bounds.Width)),
                        0, region.Width),
                    region.Y + (int)Math.Clamp(Math.Round(local.Y * region.Height / Math.Max(1, _canvas.Bounds.Height)),
                        0, region.Height));
            }
        }

        public PixelRect Region { get; }

        internal bool TryGetSampleColor(PixelPoint point, out Color color)
        {
            return _canvas.TryGetSampleColor(point, out color);
        }

        internal void ShowOperationError(string message)
        {
            _canvas.OperationError = message;
            _canvas.InvalidateVisual();
        }

        public PixelPoint Snap(PixelPoint point, int mode)
        {
            return _canvas.Snap(point, mode);
        }

        public void Refresh(PixelRect selected, PixelPoint point, int mode)
        {
            var cursor = SelectionCursors[_coordinator.ManipulationCursor(point) ?? StandardCursorType.Cross];
            if (!ReferenceEquals(Cursor, cursor)) Cursor = cursor;
            if (_lastPoint != point) _canvas.OperationError = null;
            _lastPoint = point;
            _lastMode = mode;
            _canvas.Selected = selected;
            _canvas.Snapshot = _coordinator.Snapshot;
            _canvas.HintMode = _coordinator.HintMode;
            _canvas.CapturePreferences = _coordinator.CapturePreferences;
            _canvas.ApplyAppearance(_coordinator.AppearancePreferences);
            _canvas.AuxiliaryVisible = _coordinator.AuxiliaryVisible;
            _canvas.ShowCursor = _coordinator.CanToggleCursor;
            _canvas.Mouse = point;
            _canvas.SampleColorFormat = _coordinator.SampleColorFormat;
            _canvas.Mode = mode;
            if (_sourceLocal && _canvas.Bounds.Width > 0) _canvas.Scale = Region.Width / _canvas.Bounds.Width;
            _canvas.Controls = _coordinator.Controls;
            _canvas.WindowSource = _coordinator.WindowSource;
            _canvas.WindowFrame = _coordinator.FrozenWindow;
            _canvas.Manipulating = _coordinator.SelectionManipulating;
            _canvas.IsScreenshot = _coordinator.ScreenshotMode;
            _canvas.IsDrawing = _coordinator.Annotations?.IsDrawing == true;
            _canvas.IsEditing = _coordinator.ScreenshotDrawing;
            var decoration = _coordinator.Decoration;
            _canvas.SetFrameDecoration(decoration);
            _canvas.FrameShadowPreview = _coordinator.FrameShadowPreview;
            UpdateFrameClip(selected, decoration);
            if (_coordinator.ScreenshotMode)
            {
                var frame = _coordinator.FrozenWindow;
                if (!ReferenceEquals(frame, _shownWindowFrame))
                {
                    _shownWindowFrame = frame;
                    if (_cursorOverlay is not null)
                    {
                        _layers.Children.Remove(_cursorOverlay);
                        _cursorOverlay.Dispose();
                        _cursorOverlay = null;
                    }

                    if (!_sourceLocal && _coordinator.ScreenshotCursor is { } screenshotCursor &&
                        _screenPixels is not null)
                    {
                        _cursorOverlay = new(screenshotCursor, Region, frame?.Pixels ?? _screenPixels, _canvas.Scale,
                            frame?.Region);
                        _layers.Children.Insert(1, _cursorOverlay);
                    }
                }
            }

            if (_cursorOverlay is not null)
            {
                _cursorOverlay.NativeHdrBackground = _canvas.NativeHdrBackground;
                _cursorOverlay.IsVisible = _coordinator.Snapshot.Cursor;
                _cursorOverlay.Selection = selected;
                _cursorOverlay.Scale = _canvas.Scale;
                _cursorOverlay.InvalidateVisual();
            }

            _annotationSurface?.Scale = _canvas.Scale;
            _canvas.ReservedBounds = default;
            if (_screenshotToolbar is not null)
            {
                _screenshotToolbar.Scale = _canvas.Scale;
                _screenshotToolbar.Refresh(selected, point, _coordinator.OwnsScreenshotToolbar(this));
                if (_screenshotToolbar.IsVisible) _canvas.ReservedBounds = _screenshotToolbar.OccupiedBounds;
            }

            if (_selectionActions is not null)
            {
                _selectionActions.Update(Region, _canvas.Scale, _coordinator.Snapshot,
                    _coordinator.OwnsScreenshotToolbar(this), _coordinator.SelectionManipulating);
                _canvas.ReservedBounds = _selectionActions.OccupiedBounds;
            }

            _selectionOptions.Update(_coordinator.Snapshot, _coordinator.CanDetectControls,
                _coordinator.CanSwitchSource, _coordinator.CanToggleCursor, _coordinator.ScreenshotDrawing);
            _selectionOptions.Place(Region, _canvas.Scale, selected, _canvas.ReservedBounds);
            _canvas.SecondaryReservedBounds = _selectionOptions.OccupiedBounds;
            _frameControls.Update(Region, _canvas.Scale, selected, _coordinator.OwnsScreenshotToolbar(this),
                _canvas.ReservedBounds, _canvas.SecondaryReservedBounds);
            _canvas.FrameReservedBounds = _frameControls.OccupiedBounds;

            RefreshHdrBackground();
            _canvas.InvalidateVisual();
        }

        private void AnnotationChanged()
        {
            Refresh(_coordinator.Snapshot.Region, _lastPoint, _lastMode);
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
            DisposeHdrBackground();
            _mouseClicks.Dispose();
            if (_annotationSession is not null) _annotationSession.Changed -= AnnotationChanged;
            _annotationSurface?.Dispose();
            _cursorOverlay?.Dispose();
            _screenshotToolbar?.Dispose();
            _frameControls.Dispose();
            _canvas.Dispose();
        }
    }
}
