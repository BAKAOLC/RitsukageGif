using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ImageEditorWindow : Window
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.ImageEditor");
        private readonly Grid _editorRoot = new() { RowDefinitions = new("Auto,*,Auto") };
        private readonly Grid _imageLayer;
        private readonly AnnotationPreferences _initialPreferences;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Func<Window, ScreenshotResult, CancellationToken, Task<bool>> _output;
        private readonly SemaphoreSlim _previewGate = new(1, 1);
        private readonly SimulatedWheelInput _simulatedWheel;
        private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly ImageViewport _viewport;
        private readonly TextBlock _zoom = new() { VerticalAlignment = VerticalAlignment.Center };
        private ImageEditorCanvas _canvas;
        private bool _closed;
        private CapturedFrame? _hdrSource;
        private ScreenshotTool _lastDrawingTool = ScreenshotTool.Brush;
        private bool _outputBusy;
        private long _previewVersion;
        private CancellationTokenSource? _previewWork;
        private PixelRect _sourceBounds;
        private byte[] _sourcePixels;
        private byte[] _sourcePng;
        private ScreenshotAnnotationSurface _surface;
        private ScreenshotToolbar _toolbar;

        internal ImageEditorWindow(PinnedImageContent image, ScreenshotResult? source,
            AnnotationPreferences preferences, IReadOnlyList<SelectionCommandDefinition> bindings,
            Func<Window, ScreenshotResult, CancellationToken, Task<bool>> output)
        {
            _simulatedWheel = new(this);
            _sourceBounds = new(0, 0, image.Width, image.Height);
            _sourcePng = image.Hdr is null ? image.Png : image.SdrPng ?? image.Png;
            _sourcePixels = image.Pixels;
            _hdrSource = source?.Hdr ?? image.Hdr;
            _output = output;
            Width = 1280;
            Height = 800;
            MinWidth = 1280;
            MinHeight = 720;
            Topmost = true;
            Background = AppTheme.Workspace;
            Foreground = AppTheme.Ink;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Bind(TitleProperty, LiveText.Resource("ImageEditorTitle"));
            Session.ApplyPreferences(preferences);
            Session.Tool = ScreenshotTool.Select;
            _initialPreferences = Session.CapturePreferences(preferences) with { LastTool = preferences.LastTool };
            Session.RegisterFrame(_sourceBounds, _sourcePixels);
            Session.NotifySelection(_sourceBounds);
            Session.CanOutputHdr = _hdrSource is not null;
            Session.OutputColorMode = source?.ColorMode ?? OutputColorMode.Sdr;
            _canvas = new(image.Width, image.Height, _sourcePixels);
            _imageLayer = new() { Width = image.Width, Height = image.Height, ClipToBounds = true };
            _imageLayer.Children.Add(_canvas);
            _surface = new(Session, _sourceBounds, 1, args =>
            {
                var point = args.GetPosition(_imageLayer);
                return new((int)Math.Floor(point.X), (int)Math.Floor(point.Y));
            });
            _imageLayer.Children.Add(_surface);
            _viewport = new() { ImageContent = _imageLayer, PixelSize = new(image.Width, image.Height) };
            _toolbar = new(Session, _sourceBounds, 1, bindings, ExecuteCommand, true)
                { IsDocked = true, Margin = new(6) };
            var body = _editorRoot;
            body.Children.Add(_toolbar);
            Grid.SetRow(_viewport, 1);
            body.Children.Add(_viewport);
            var footer = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto,Auto,Auto"), Margin = new(8, 4) };
            var hint = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            hint.Bind(TextBlock.TextProperty, LiveText.Resource("ImageEditorModeHint"));
            footer.Children.Add(new StackPanel
                { Orientation = Orientation.Horizontal, Spacing = 12, Children = { hint, _status } });
            AddZoomButton(footer, 1, "AnimationZoomOut", () => _viewport.SetZoom(_viewport.Zoom / 1.25));
            Grid.SetColumn(_zoom, 2);
            footer.Children.Add(_zoom);
            AddZoomButton(footer, 3, "AnimationZoomIn", () => _viewport.SetZoom(_viewport.Zoom * 1.25));
            AddZoomButton(footer, 4, "AnimationActualSize", _viewport.ActualSize);
            AddZoomButton(footer, 5, "AnimationFit", _viewport.FitToViewport);
            Grid.SetRow(footer, 2);
            body.Children.Add(footer);
            Content = body;
            AttachDocumentSession();
            _viewport.ViewChanged += (_, _) => ViewChanged();
            InitializeStripEditing();
            AddHandler(KeyDownEvent, EditorKeyDown, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, (_, args) =>
            {
                Session.ReleasePolylineArrowKey(args.Key);
                if (Session.HandleNumberCaptionKey(args.Key, args.KeyModifiers, false)) args.Handled = true;
            }, RoutingStrategies.Tunnel);
            Deactivated += (_, _) =>
            {
                Session.ReleaseNumberCaptionKey();
                Session.ReleasePolylineArrowKey();
            };
            Opened += (_, _) =>
            {
                InitializeHdrPreview();
                ViewChanged();
            };
            Closed += (_, _) => CloseEditor();
            SessionChanged();
            Logger.ZLogInformation(
                $"Static image editor created. Width={image.Width} Height={image.Height} Hdr={_hdrSource is not null}");
        }

        internal Task CleanupCompletion { get; private set; } = Task.CompletedTask;

        internal ScreenshotAnnotationSession Session { get; private set; } = new() { CanScroll = false };

        internal CapturedFrame SourceFrame => new(_sourceBounds.Width, _sourceBounds.Height,
            checked(_sourceBounds.Width * 4), CapturePixelFormat.Bgra8Srgb, _sourcePixels,
            new(false), CaptureAlphaMode.Straight, 0, []);

        internal Func<AnnotationPreferences>? PreferencesProvider { get; init; }
        internal Action<AnnotationPreferences>? PreferencesChanged { get; init; }
        internal Action<bool>? TopmostChanged { get; init; }

        internal Func<Window, byte[], RecognitionMode, bool, CancellationToken, Task>? RecognitionRequested
        {
            get;
            init;
        }

        internal Func<Window, byte[], CancellationToken, Task>? TranslationRequested { get; init; }
        internal Func<long, CancellationToken, Task>? PrivacyRequested { get; set; }

        internal void ConnectServices()
        {
            _servicesConnected = true;
            if (!_serviceSessions.Add(Session)) return;
            Session.ToolbarPreferencesProvider = () => (PreferencesProvider?.Invoke() ?? _initialPreferences).Toolbar;
            Session.ToolbarPreferencesChanged += (toolbar, tools) =>
            {
                if (PreferencesProvider?.Invoke() is not { } current) return;
                PreferencesChanged?.Invoke(current with { Toolbar = current.Toolbar.Merge(toolbar, tools) });
            };
            Session.RecognitionRequested += mode => _ = RunAsync(async () =>
            {
                if (RecognitionRequested is not null)
                    await RecognitionRequested(this, await ComposeSdrAsync(), mode, false, _lifetime.Token);
            });
            Session.TranslationRequested += () => _ = RunAsync(async () =>
            {
                if (TranslationRequested is not null)
                    await TranslationRequested(this, await ComposeSdrAsync(), _lifetime.Token);
            });
            Session.PrivacyDetectionRequested += request => _ = RunAsync(async () =>
            {
                if (PrivacyRequested is { } detect) await detect(request, Session.PrivacyDetectionToken);
            });
        }

        private static void AddZoomButton(Grid grid, int column, string key, Action action)
        {
            var button = new Button { Padding = new(6), Margin = new(2, 0) };
            button.Content = ToolbarIcon.Create(button, ToolbarIcon.GetDefinition(key));
            button.Bind(ToolTip.TipProperty, LiveText.Resource(key));
            button.Click += (_, _) => action();
            Grid.SetColumn(button, column);
            grid.Children.Add(button);
        }

        private void SessionChanged()
        {
            if (_closed || _documentChanging) return;
            if (IsStripEditing && Session.Tool != ScreenshotTool.Select) SetStripMode(null);
            var pan = Session.Tool == ScreenshotTool.Select;
            if (!pan) _lastDrawingTool = Session.Tool;
            _viewport.PanEnabled = pan;
            _surface.IsHitTestVisible = !pan && Session.CanAnnotate;
            _toolbar.Refresh(_sourceBounds, default, true);
            RefreshDocumentButtons();
            QueuePreview();
        }

        private void ViewChanged()
        {
            _surface.ControlScale = RenderScaling / _viewport.Zoom;
            _zoom.Text = $"{_viewport.Zoom * 100:0.#}%";
            _canvas.SetVisibleRegion(_viewport.ExactVisibleContentRect);
            _stripSurface?.InvalidateVisual();
            PositionStripButton();
            QueuePreview();
        }

        private void SetEditorStatus(string? text)
        {
            _status.Text = text;
        }

        private void SetEditorError(Exception error)
        {
            Logger.ZLogError(error, $"Static image editor operation failed.");
            if (!_closed) SetEditorStatus(error.Message);
        }

        private void SetNativePreviewVisible(bool visible)
        {
            _canvas.NativePreviewVisible = visible;
            RefreshSurfaceRendering();
        }

        private void RefreshSurfaceRendering()
        {
            _surface.RenderContent = _canvas is { NativePreviewVisible: false, HasPreview: false };
            _surface.InvalidateVisual();
        }

        private async Task RunAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                SetEditorError(error);
            }
        }

        private void CloseEditor()
        {
            if (_closed) return;
            _closed = true;
            _stripPointer?.Capture(null);
            _stripPointer = null;
            _lifetime.Cancel();
            _previewWork?.Cancel();
            StopHdrPreview();
            if (PreferencesProvider?.Invoke() is { } preferences)
            {
                var edited = Session.CapturePreferences(_initialPreferences) with
                {
                    LastTool = _initialPreferences.LastTool,
                };
                var merged = AppSettings.MergeAnnotationChanges(preferences, _initialPreferences, edited);
                if (!ReferenceEquals(merged, preferences)) PreferencesChanged?.Invoke(merged);
            }

            Session.Changed -= SessionChanged;
            Session.TextEditStateChanged -= QueuePreview;
            _toolbar.Dispose();
            _surface.Dispose();
            CleanupCompletion = DisposeDocumentAsync();
            Logger.ZLogInformation($"Static image editor closed.");
        }

        private async Task DisposeDocumentAsync()
        {
            await _previewGate.WaitAsync();
            try
            {
                Session.ClearImagePreviewCache();
                Session.Dispose();
                DisposeDocumentHistory();
                _canvas.Dispose();
                _lifetime.Dispose();
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Static image editor cleanup failed.");
            }
            finally
            {
                _previewGate.Release();
            }
        }

        partial void InitializeHdrPreview();
        partial void QueueHdrPreview();
        partial void StopHdrPreview();
    }
}
