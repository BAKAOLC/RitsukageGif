using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayWindow
    {
        private static readonly ILogger ScrollingHdrLogger = LogHub.GetLogger("Desktop.Capture.ScrollingPreview");
        private bool _scrollHdrClosed;
        private Rect _scrollHdrComposedBounds;
        private PixelSize _scrollHdrComposedSize;
        private CapturedFrame? _scrollHdrComposedSource, _scrollHdrComposedFrame;
        private long _scrollHdrContentVersion, _scrollHdrComposedVersion;
        private Grid? _scrollHdrLayers;
        private Point? _scrollHdrPointer;
        private HdrPreviewHost? _scrollHdrPreview;
        private bool _scrollHdrQueued;
        private bool _scrollHdrRunning;

        private long _scrollHdrVersion;

        private (SelectionSnapshot? Snapshot, PixelPoint Point, int Mode, CaptureDecoration Decoration,
            AppearancePreferences Appearance, CapturePreferences Capture, SelectionDecorationPreview? Shadow,
            ThemeVariant Theme, bool Picker, Rect PickerBounds, QrDetectionSnapshot? Codes,
            bool CodeVisible, bool RatioLocked)? _scrollHdrUiState;
        private Rect _scrollHdrViewport;
        private CancellationTokenSource? _scrollHdrWork;

        private void ConfigureScrollingHdrPreview(Grid layers)
        {
            if (!HdrPreviewHost.IsSupported || _annotationSession is null) return;
            _scrollHdrLayers = layers;
            if (_annotationSurface is not null)
            {
                _annotationSurface.AddHandler(PointerMovedEvent, (_, args) =>
                {
                    var local = args.GetPosition(_canvas);
                    _scrollHdrPointer = new(
                        Region.X + Math.Clamp(Math.Round(local.X * Region.Width / Math.Max(1, _canvas.Bounds.Width)), 0,
                            Region.Width),
                        Region.Y + Math.Clamp(Math.Round(local.Y * Region.Height / Math.Max(1, _canvas.Bounds.Height)),
                            0, Region.Height));
                    QueueScrollingHdrPreview();
                }, RoutingStrategies.Bubble, true);
                _annotationSurface.PointerExited += (_, _) =>
                {
                    _scrollHdrPointer = null;
                    QueueScrollingHdrPreview();
                };
            }

            _annotationSession.Changed += ScrollingHdrContentChanged;
            _annotationSession.TextEditStateChanged += ScrollingHdrContentChanged;
            Opened += (_, _) =>
            {
                _frameControls.LayoutChanged += QueueScrollingHdrPreview;
                QueueScrollingHdrPreview();
            };
            Closed += (_, _) =>
            {
                _scrollHdrClosed = true;
                _frameControls.LayoutChanged -= QueueScrollingHdrPreview;
                _annotationSession.Changed -= ScrollingHdrContentChanged;
                _annotationSession.TextEditStateChanged -= ScrollingHdrContentChanged;
                _scrollHdrWork?.Cancel();
                _scrollHdrPreview?.Dispose();
            };
        }

        private void ScrollingHdrContentChanged()
        {
            _scrollHdrContentVersion++;
            QueueScrollingHdrPreview();
        }

        private void QueueScrollingHdrPreview(CaptureDecoration decoration)
        {
            if (_scrollHdrClosed || _scrollHdrLayers is null || _coordinator.HdrScreenshotFor(Region) is null) return;
            var state = (_canvas.Snapshot, _canvas.Mouse, _canvas.Mode, decoration,
                _coordinator.AppearancePreferences, _canvas.CapturePreferences, _canvas.FrameShadowPreview,
                ActualThemeVariant, _shapeModePicker.IsVisible, _shapeModePicker.Bounds, _coordinator.DetectedCodes,
                _coordinator.CodeOverlayVisible, _coordinator.GeometryRatioLocked);
            if (_scrollHdrUiState == state) return;
            _scrollHdrUiState = state;
            QueueScrollingHdrPreview();
        }

        private void QueueScrollingHdrPreview()
        {
            if (_scrollHdrClosed || _scrollHdrLayers is null || _coordinator.HdrScreenshotFor(Region) is null) return;
            _scrollHdrVersion++;
            _scrollHdrWork?.Cancel();
            if (_annotationSession?.IsEditingText == true)
            {
                _scrollHdrPreview?.SetFrame(null);
                return;
            }

            if (_scrollHdrQueued) return;
            _scrollHdrQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _scrollHdrQueued = false;
                if (!_scrollHdrClosed) _ = UpdateScrollingHdrPreviewAsync(_scrollHdrVersion);
            }, DispatcherPriority.Render);
        }

        private async Task UpdateScrollingHdrPreviewAsync(long version)
        {
            if (_scrollHdrRunning) return;
            if (_annotationSession is not { IsEditingText: false } ||
                _scrollHdrLayers is not { } layers ||
                _coordinator.HdrScreenshotFor(Region) is not { } original || !IsVisible) return;
            var visible = _scrollHdrViewport.Intersect(new(0, 0, original.Width, original.Height));
            if (visible.Width <= 0 || visible.Height <= 0)
            {
                _scrollHdrPreview?.SetFrame(null);
                return;
            }

            _scrollHdrRunning = true;
            using var work = new CancellationTokenSource();
            _scrollHdrWork = work;
            try
            {
                if (_scrollHdrPreview is null)
                {
                    _scrollHdrPreview = new()
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                    };
                    _scrollHdrPreview.DisplayColorChanged += QueueScrollingHdrPreview;
                    layers.Children.Insert(layers.Children.IndexOf(_canvas) + 1, _scrollHdrPreview);
                }

                _scrollHdrPreview.Margin = new(visible.X / _canvas.Scale, visible.Y / _canvas.Scale, 0, 0);
                _scrollHdrPreview.Width = visible.Width / _canvas.Scale;
                _scrollHdrPreview.Height = visible.Height / _canvas.Scale;
                var width = Math.Max(1, (int)Math.Ceiling(_scrollHdrPreview.Width * RenderScaling));
                var height = Math.Max(1, (int)Math.Ceiling(_scrollHdrPreview.Height * RenderScaling));
                var reduction = Math.Min(1, Math.Sqrt(1048576d / ((long)width * height)));
                var size = new PixelSize(Math.Max(1, (int)(width * reduction)), Math.Max(1, (int)(height * reduction)));
                var contentVersion = _scrollHdrContentVersion;
                if (_scrollHdrComposedFrame is null || !ReferenceEquals(_scrollHdrComposedSource, original) ||
                    _scrollHdrComposedVersion != contentVersion || _scrollHdrComposedBounds != visible ||
                    _scrollHdrComposedSize != size)
                {
                    _scrollHdrComposedFrame =
                        await _annotationSession.ComposeImageHdrPreviewAsync(original, visible, size, work.Token);
                    _scrollHdrComposedSource = original;
                    _scrollHdrComposedVersion = contentVersion;
                    _scrollHdrComposedBounds = visible;
                    _scrollHdrComposedSize = size;
                }

                var frame = _scrollHdrComposedFrame!;
                work.Token.ThrowIfCancellationRequested();
                using var transient = new RenderTargetBitmap(size, new(96, 96));
                using (var context = transient.CreateDrawingContext())
                {
                    using (context.PushTransform(new(size.Width / visible.Width, 0, 0,
                               size.Height / visible.Height, -visible.X * size.Width / visible.Width,
                               -visible.Y * size.Height / visible.Height)))
                    {
                        var controlScale = _annotationSurface?.ControlScale ?? _canvas.Scale;
                        _annotationSession.RenderSelection(context, controlScale);
                        if (_scrollHdrPointer is { } pointer)
                            _annotationSession.RenderPencilCursor(context, pointer, controlScale);
                    }

                    using var controls = context.PushTransform(new(size.Width * _canvas.Scale / visible.Width, 0, 0,
                        size.Height * _canvas.Scale / visible.Height, -visible.X * size.Width / visible.Width,
                        -visible.Y * size.Height / visible.Height));
                    _canvas.RenderHdrOverlay(context);
                    foreach (var child in layers.Children)
                    {
                        var control = child is ThemeVariantScope { Child: { } scoped } ? scoped : child;
                        if (ReferenceEquals(control, _canvas) || ReferenceEquals(control, _annotationSurface) ||
                            ReferenceEquals(control, _scrollHdrPreview) || !control.IsVisible ||
                            control.Bounds.Width <= 0 || control.Bounds.Height <= 0) continue;
                        if (control.TranslatePoint(default, layers) is not { } origin) continue;
                        var clipped = new Rect(origin, control.Bounds.Size).Intersect(new(visible.X / _canvas.Scale,
                            visible.Y / _canvas.Scale, visible.Width / _canvas.Scale, visible.Height / _canvas.Scale));
                        if (clipped.Width <= 0 || clipped.Height <= 0) continue;
                        var brush = new VisualBrush(control)
                        {
                            SourceRect = new(
                                new Rect(clipped.X - origin.X, clipped.Y - origin.Y, clipped.Width, clipped.Height),
                                RelativeUnit.Absolute),
                        };
                        context.DrawRectangle(brush, null, clipped);
                    }
                }

                using var overlay = new ImagePreviewFramebuffer(size, true);
                transient.CopyPixels(overlay);
                var white = HdrPreviewHost.DisplayWhiteLevel(TryGetPlatformHandle()!.Handle,
                    frame.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits);
                using var preview = await Task.Run(() => HdrImagePreviewBuilder.Build(frame, visible, overlay.Pixels,
                    white, Colors.Black, work.Token), work.Token);
                if (_scrollHdrClosed || version != _scrollHdrVersion) return;
                _scrollHdrPreview.SetFrame(preview);
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                ScrollingHdrLogger.ZLogWarning(error, $"Scrolling editor HDR preview unavailable.");
                if (!_scrollHdrClosed) _scrollHdrPreview?.SetFrame(null);
            }
            finally
            {
                if (ReferenceEquals(_scrollHdrWork, work)) _scrollHdrWork = null;
                _scrollHdrRunning = false;
                if (!_scrollHdrClosed && version != _scrollHdrVersion) QueueScrollingHdrPreview();
            }
        }
    }
}
