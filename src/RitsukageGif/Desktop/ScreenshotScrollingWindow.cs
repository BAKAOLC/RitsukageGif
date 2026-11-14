using System.Runtime.InteropServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingWindow : Window
    {
        private static readonly ILogger ScrollingUiLogger = LogHub.GetLogger("Desktop.Capture.ScrollingWindow");
        private static long _nextScrollingOperation;
        private readonly AppearancePreferences _appearance;

        private readonly Action<bool>? _autoCropChanged;
        private readonly List<Window> _borders = [];
        private readonly Action<bool>? _clearConfirmationChanged;

        private readonly TaskCompletionSource<bool>
            _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ComboBox _direction = new()
            { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28 };

        private readonly Button _finish = new() { IsEnabled = false };
        private readonly List<ScreenshotScrollingMaskWindow> _maskWindows = [];
        private readonly PixelRect? _preferredPlacementScreen;
        private readonly ScreenshotScrollingPreview _preview = new() { Height = 100 };
        private readonly Screens _screens;
        private readonly ScreenshotScrollingSession _session;
        private readonly TextBlock _size = new();
        private readonly bool _startAutomatically;
        private readonly Button _startStop = new();
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
        private readonly bool _windowSource;
        private ScrollCaptureAxis _axis;
        private WriteableBitmap? _bitmap;
        private string? _captureFailure;
        private bool _captureInitialized;
        private bool _captureStarted;
        private ScreenshotScrollingClearDialog? _clearDialog;
        private bool _closed;

        private PixelRect _dragRegion;
        private bool _dragStartedWhileCapturing;
        private Avalonia.PixelPoint? _dragStart;
        private bool _excluded;
        private ScrollCaptureStatus? _lastCaptureStatus;
        private bool _obstructed;
        private PixelRect _region;
        private bool _skipClearConfirmation;
        private string? _statusNoticeKey;
        private string? _boundStatusKey;
        private bool _updatingDirection;

        private ScreenshotScrollingWindow(Screens screens, PixelRect region, ScrollCaptureAxis axis,
            WindowTarget? target, HdrToneMapMode toneMap, FrozenScreenshotCursor? cursor, bool respondShortcuts = true,
            PixelRect? preferredPlacementScreen = null, AppearancePreferences? appearance = null,
            bool autoCropOnReverse = false, Action<bool>? autoCropChanged = null,
            OutputColorMode colorMode = OutputColorMode.Sdr,
            ScrollCaptureInitialAction initialAction = ScrollCaptureInitialAction.DoNotStartAutomatically,
            bool skipClearConfirmation = false, Action<bool>? clearConfirmationChanged = null,
            ScrollCapturePreferences? warningPreferences = null,
            Action<ScrollCaptureWarningKind>? warningSuppressed = null)
        {
            _autoCropChanged = autoCropChanged;
            _clearConfirmationChanged = clearConfirmationChanged;
            _skipClearConfirmation = skipClearConfirmation;
            _warningPreferences = warningPreferences ?? new();
            _warningSuppressed = warningSuppressed;
            _screens = screens;
            _appearance = (appearance ?? new AppearancePreferences()).Normalize();
            _preferredPlacementScreen = preferredPlacementScreen;
            _region = region;
            _axis = axis;
            _startAutomatically =
                initialAction is ScrollCaptureInitialAction.Vertical or ScrollCaptureInitialAction.Horizontal;
            _windowSource = target is not null;
            _session = new(region, axis, [
                .. screens.All.Select(screen => new PixelRect(screen.Bounds.X,
                    screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height)),
            ], target, toneMap, cursor, autoCropOnReverse, colorMode != OutputColorMode.Sdr);
            if (!_warningPreferences.IsWarningSuppressed(ScrollCaptureWarningKind.FileOnly))
                _session.ConfigureFileOnlyWarning(FileOnlyWarningLength);
            Bind(TitleProperty, LiveText.Resource("ScrollCaptureTitle"));
            var panelScreen = FindPanelScreen(region, [
                .. screens.All.Select(screen => (new PixelRect(screen.WorkingArea.X, screen.WorkingArea.Y,
                    screen.WorkingArea.Width, screen.WorkingArea.Height), screen.Scaling)),
            ], _preferredPlacementScreen);
            MaxWidth = Math.Max(1, panelScreen.Area.Width / panelScreen.Scale - 24);
            WindowDecorations = WindowDecorations.None;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            Topmost = true;
            TransientWindowShell.Configure(this);
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.Manual;
            InitializeShortcutHandling(respondShortcuts);
            Content = BuildControls(target, respondShortcuts);
            _direction.SelectionChanged += (_, _) =>
            {
                if (_updatingDirection || _direction.SelectedIndex < 0) return;
                if (_outputting || _resizePointer is not null)
                {
                    RefreshDirection();
                    return;
                }

                var next = _direction.SelectedIndex == 0 ? ScrollCaptureAxis.Vertical : ScrollCaptureAxis.Horizontal;
                if (next == _axis) return;
                ResetCapture(next, 0, 0, _startAutomatically);
            };
            SizeChanged += (_, _) =>
            {
                if (IsVisible && _dragStart is null) Reposition();
            };
            RefreshStatus();
            _session.Updated += OnUpdated;
            _session.Failed += OnFailed;
            Opened += (_, _) =>
            {
                _excluded = Exclude(this);
                EnsureShortcutScope();
                UpdateBorders();
                Reposition();
                if (!_captureInitialized)
                {
                    _session.ConfigureInsets(0, 0);
                    _captureInitialized = true;
                    if (_startAutomatically) StartCapture();
                }

                SetStartStopContent();
                RefreshStatus();
            };
            PositionChanged += (_, _) => UpdateObstruction();
            Closed += (_, _) =>
            {
                _closed = true;
                _clearDialog?.Close(false);
                _warningDialog?.Close(false);
                DisposeShortcutScope();
                _cropPopup.IsOpen = false;
                CloseLivePreview();
                CloseResizeTargets();
                _session.AutomaticEnabled = false;
                _session.AutomaticChanged -= AutomaticChanged;
                UiText.LanguageChanged -= RefreshDirection;
                _completed.TrySetResult(false);
                foreach (var border in _borders) border.Close();
                _borders.Clear();
                foreach (var mask in _maskWindows) mask.Close();
                _maskWindows.Clear();
                _preview.Image = null;
                _preview.DisposeHdr();
                _bitmap?.Dispose();
                _bitmap = null;
                _session.Updated -= OnUpdated;
                _session.Failed -= OnFailed;
            };
        }

        private void RefreshDirection()
        {
            _updatingDirection = true;
            try
            {
                _direction.ItemsSource = new[]
                    { UiText.Get("ScrollCaptureAxisVertical"), UiText.Get("ScrollCaptureAxisHorizontal") };
                _direction.SelectedIndex = _axis == ScrollCaptureAxis.Vertical ? 0 : 1;
            }
            finally
            {
                _updatingDirection = false;
            }
        }

        private void AddShortcutHint(Button button, string action)
        {
            var content = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 18 };
            var label = new TextBlock { [!TextBlock.TextProperty] = button[!AutomationProperties.NameProperty] };
            var hint = new TextBlock { Foreground = AppTheme.Muted };
            Grid.SetColumn(hint, 1);
            content.Children.Add(label);
            content.Children.Add(hint);
            button.ClearValue(ToolTip.TipProperty);
            ToolTip.SetTip(button, content);
            RefreshHint();
            _shortcutHintRefreshers.Add(RefreshHint);
            button.PointerEntered += (_, _) => RefreshHint();
            return;

            void RefreshHint()
            {
                var gesture = _longshotBindings.FirstOrDefault(item => item.Action == action).Gesture;
                hint.Text = gesture is null ? "" : KeyGestureDisplay.Format(gesture);
                hint.IsVisible = gesture is not null;
            }
        }

        private void SetStartStopContent()
        {
            SetActionContent(_startStop, "ScrollCaptureStartStop",
                ToolbarIcon.GetDefinition(_captureStarted ? "ScrollCaptureClear" : "ScrollCaptureStart"));
        }

        private static void SetActionContent(Button button, string key, IconDefinition geometry)
        {
            button.Padding = new(5);
            button.Width = button.Height = 32;
            button.Focusable = false;
            button.Foreground = AppTheme.Ink;
            button.Background = Brushes.Transparent;
            button.BorderThickness = new(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            var icon = ToolbarIcon.Create(button, geometry);
            icon.Width = icon.Height = 20;
            button.Content = icon;
            button.Bind(AutomationProperties.NameProperty, LiveText.Resource(key));
            if (ToolTip.GetTip(button) is not Control)
                ToolTip.SetTip(button, new TextBlock
                {
                    [!TextBlock.TextProperty] = button[!AutomationProperties.NameProperty],
                });
        }

        public static async Task<ScrollingCaptureResult?> RunAsync(Screens screens, PixelRect region,
            ScrollCaptureAxis axis,
            WindowTarget? target, CancellationToken token,
            HdrToneMapMode toneMap = HdrToneMapMode.HighlightCompression, FrozenScreenshotCursor? cursor = null,
            bool respondShortcuts = true, PixelRect? preferredPlacementScreen = null,
            AppearancePreferences? appearance = null, Func<CancellationToken, Task>? prepareCapture = null,
            Func<Window, ScrollCaptureImage, ScrollingCaptureAction, CancellationToken, Task<bool>>? outputHandler =
                null, bool autoCropOnReverse = false, Action<bool>? autoCropChanged = null,
            OutputColorMode colorMode = OutputColorMode.Sdr,
            ScrollCaptureInitialAction initialAction = ScrollCaptureInitialAction.DoNotStartAutomatically,
            bool skipClearConfirmation = false, Action<bool>? clearConfirmationChanged = null,
            bool saveOnUserClose = false, ScrollCapturePreferences? warningPreferences = null,
            Action<ScrollCaptureWarningKind>? warningSuppressed = null)
        {
            Dispatcher.UIThread.VerifyAccess();
            token.ThrowIfCancellationRequested();
            var operationId = Interlocked.Increment(ref _nextScrollingOperation);
            using var scope = ScrollingUiLogger.BeginScope("ScrollingOperation {ScrollingOperationId}", operationId);
            ScrollingUiLogger.ZLogInformation(
                $"Scrolling window initialization started. OperationId={operationId} Region={region} Axis={axis.ToString()} WindowSource={target is not null} PreferredScreen={preferredPlacementScreen}");
            var window =
                new ScreenshotScrollingWindow(screens, region, axis, target, toneMap, cursor, respondShortcuts,
                    preferredPlacementScreen, appearance, autoCropOnReverse, autoCropChanged, colorMode, initialAction,
                    skipClearConfirmation, clearConfirmationChanged, warningPreferences, warningSuppressed);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var captureToken = cancellation.Token;
            window._outputToken = captureToken;
            window._saveOnUserClose = saveOnUserClose;
            window._outputHandler = outputHandler;
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            Task? running = null;
            window.Closed += CancelCapture;
            try
            {
                window.Show();
                WindowDiagnostics.Write(ScrollingUiLogger, "ScrollingWindowShown", window);
                window.UpdateLayout();
                if (ElementComposition.GetElementVisual(window) is { } visual)
                {
                    ScrollingUiLogger.ZLogDebug($"Scrolling first render requested. OperationId={operationId}");
                    await visual.Compositor.RequestCompositionBatchCommitAsync().Rendered
                        .WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);
                    ScrollingUiLogger.ZLogDebug($"Scrolling first render completed. OperationId={operationId}");
                }

                await CaptureWindowVisibility.FlushAsync(captureToken);
                ScrollingUiLogger.ZLogDebug($"Scrolling window composition ready. OperationId={operationId}");
                if (prepareCapture is not null) await prepareCapture(cancellation.Token);
                ScrollingUiLogger.ZLogDebug($"Scrolling overlay handoff returned. OperationId={operationId}");
                cancellation.Token.ThrowIfCancellationRequested();
                if (window._closed) return null;
                window.Activate();
                running = Task.Run(() => window._session.RunAsync(captureToken), captureToken);
                ScrollingUiLogger.ZLogInformation($"Scrolling capture worker started. OperationId={operationId}");
                var first = await Task.WhenAny(window._completed.Task, running);
                if (first == running && !window._completed.Task.IsCompleted) window.Close();
                var accepted = await window._completed.Task;
                ScrollingUiLogger.ZLogInformation(
                    $"Scrolling capture completion requested. OperationId={operationId} Accepted={accepted}");
                await cancellation.CancelAsync();
                await ObserveRunning();
                token.ThrowIfCancellationRequested();
                return accepted ? window._outputResult : null;
            }
            catch (OperationCanceledException) when (window._closed && !token.IsCancellationRequested)
            {
                ScrollingUiLogger.ZLogDebug(
                    $"Scrolling window closed before capture became ready. OperationId={operationId}");
                return null;
            }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            {
                ScrollingUiLogger.ZLogError(error, $"Scrolling window operation failed. OperationId={operationId}");
                throw;
            }
            finally
            {
                try
                {
                    await cancellation.CancelAsync();
                    if (!window._closed) window.Close();
                    try
                    {
                        await ObserveRunning();
                    }
                    finally
                    {
                        try
                        {
                            if (window._outputTask is { } outputTask) await outputTask;
                        }
                        finally
                        {
                            if (window._warningTask is { } warningTask) await warningTask;
                        }
                    }
                }
                finally
                {
                    window.Closed -= CancelCapture;
                    ScrollingUiLogger.ZLogInformation(
                        $"Scrolling window operation ended. OperationId={operationId} Closed={window._closed}");
                }
            }

            void CancelCapture(object? sender, EventArgs args)
            {
                // ReSharper disable once AccessToDisposedClosure
                cancellation.Cancel();
            }

            async Task ObserveRunning()
            {
                if (running is null) return;
                try
                {
                    await running;
                }
                catch (OperationCanceledException) when (captureToken.IsCancellationRequested)
                {
                }
            }
        }

        private void OnFailed(string message)
        {
            var generation = _session.Generation;
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed || generation != _session.Generation) return;
                _captureFailure = message;
                RefreshStatus();
                SetStartStopContent();
                RefreshLivePreview();
            });
        }

        private void OnUpdated(ScrollCaptureResult result, ScrollCaptureImage? preview, bool canUndo, int startOffset)
        {
            var generation = _session.Generation;
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed || generation != _session.Generation) return;
                SetOutputActionsEnabled(!_outputting && result is { Width: > 0, Height: > 0 });
                _size.Text = $"{result.Width} × {result.Height}";
                _lastCaptureStatus = result.Status;
                ObserveCaptureWarnings(result.Status);
                if (result.Status is ScrollCaptureStatus.Started or ScrollCaptureStatus.Appended
                    or ScrollCaptureStatus.AlreadyCovered)
                    _captureFailure = null;
                RefreshStatus();
                SetStartStopContent();
                _preview.ImageSize = new(result.Width, result.Height);
                _preview.Matched = _session.HasMatchedViewport;
                _preview.Viewport = _session.PreviewViewport;
                _preview.InvalidateVisual();
                if (preview is null)
                {
                    _preview.UpdateHdr(null);
                    RefreshLivePreview();
                    return;
                }

                var bitmap = new WriteableBitmap(new(preview.Width, preview.Height), new(96, 96), PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul);
                using (var buffer = bitmap.Lock())
                    for (var y = 0; y < preview.Height; y++)
                        Marshal.Copy(preview.Pixels, y * preview.Width * 4, buffer.Address + y * buffer.RowBytes,
                            preview.Width * 4);
                _preview.Image = bitmap;
                _preview.UpdateHdr(preview);
                _bitmap?.Dispose();
                _bitmap = bitmap;
                RefreshLivePreview();
            });
        }

        private void RefreshStatus()
        {
            if (!_obstructed && _captureFailure is { } failure)
            {
                if (_boundStatusKey is not null || _status.Text != failure)
                    _status.Text = failure;
                _boundStatusKey = null;
                _status.IsVisible = true;
                return;
            }

            var notice = _obstructed
                ? "ScrollCaptureObstructed"
                : _statusNoticeKey ?? (_lastCaptureStatus == ScrollCaptureStatus.LimitReached
                    ? "ScrollCaptureLimit"
                    : _session.OutputAxisLength > MaximumNonFileOutputLength
                        ? "ScrollCaptureFileOnly"
                        : _session.OutputAxisLength > FileOnlyWarningLength
                            ? "ScrollCaptureFileOnlyWarning"
                            : null);
            var key = notice ?? (_session.Paused
                ? _captureStarted ? "ScrollCapturePaused" : "Ready"
                : _lastCaptureStatus is ScrollCaptureStatus.Unmatched or ScrollCaptureStatus.Ambiguous
                    ? "ScrollCaptureUnmatched"
                    : AutomaticStatusKey(_session.AutomaticState) ?? "ScrollCaptureCollecting");
            if (_boundStatusKey != key)
            {
                _boundStatusKey = key;
                _status.Bind(TextBlock.TextProperty, LiveText.Resource(key));
            }

            _status.IsVisible = notice is not null;
        }
    }
}
