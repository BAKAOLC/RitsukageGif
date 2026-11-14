using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow : Window
    {
        private readonly Button _audio;
        private readonly Button _camera;
        private readonly ScreenshotRecordingController _controller;
        private readonly Button _delaySelector;
        private readonly TextBlock _elapsed = new() { MinWidth = 58, VerticalAlignment = VerticalAlignment.Center };

        private readonly TextBlock _error = new()
            { Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };

        private readonly TextBlock _fps = new() { MinWidth = 44, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _fpsSelector;
        private readonly RecordingRegionOverlay _frame;
        private readonly Button _input;
        private readonly Button _move;
        private readonly Button _pause;
        private readonly Button _record;
        private readonly HoverPropertyPanel _recordModePanel;

        private readonly TextBlock _regionInfo = new()
            { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };

        private readonly Button _rerecord;
        private bool _allowClose;
        private PixelRect? _annotationDensityRegion;
        private int _annotationDivisor;
        private PixelSize _annotationProvisionalSize;
        private bool _annotationUnavailable;
        private Task _cameraInitialization = Task.CompletedTask;
        private bool _captureExcluded;
        private bool _captureHidden;
        private bool _closed;
        private bool _hasShown;
        private Avalonia.PixelPoint? _moveOrigin;
        private PixelRect _moveRegion;
        private bool? _pausedVisual;
        private PixelSize? _recordingNativeSize;
        private bool? _recordingVisual;

        internal ScreenshotRecordingWindow(ApplicationHost host, SelectionResult selection,
            Func<AnnotationPreferences> preferencesProvider, Action<AnnotationPreferences> persistPreferences)
        {
            _controller = new(host, selection);
            _controller.PrepareCapture = PrepareCaptureAsync;
            _controller.CompleteCapturePreparation = CompleteToolbarCapturePreparationAsync;
            _controller.CameraSessionChanging = ChangeCameraPreviewAsync;
            _controller.PrepareSourceExclusion = PrepareNativeSourceExclusionAsync;
            _controller.BeforeSourceRelease = ReleaseRecorderExclusionAsync;
            Bind(TitleProperty, LiveText.Resource("RecorderTitle"));
            _frame = new(region =>
            {
                if (Covers(region)) _controller.UpdateRegion(region);
            });
            _frame.LayoutChanged += InvalidateAnnotationDensity;
            Annotations = new(preferencesProvider(), preferencesProvider, persistPreferences);
            Annotations.CancelRequested += CancelAnnotationContext;
            Annotations.BlockedToolClicked += ShowBlockedRecordingToolNotice;
            _blockedToolNoticeTimer.Tick += BlockedRecordingToolNoticeTick;
            Annotations.NativeExclusionFailed += NativeAnnotationExclusionFailed;
            WindowDecorations = WindowDecorations.None;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            Topmost = true;
            Background = Brushes.Transparent;
            Foreground = AppTheme.Ink;
            FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.Manual;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            TransientWindowShell.Configure(this);
            InitializeNativePopupOwnership();
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            _delaySelector = new()
            {
                Content = _elapsed, MinHeight = 0, MinWidth = 0, Height = 30, Padding = new(3),
                Background = Brushes.Transparent, Foreground = AppTheme.Ink, BorderThickness = new(0),
                Focusable = false,
            };
            RegisterRecorderPanel(_delaySelector, BuildDelaySettings);
            strip.Children.Add(_delaySelector);
            _fpsSelector = new()
            {
                Content = _fps, MinHeight = 0, MinWidth = 0, Height = 30, Padding = new(3),
                Background = Brushes.Transparent, Foreground = AppTheme.Ink, BorderThickness = new(0),
                Focusable = false,
            };
            _fpsSelector.Bind(ToolTip.TipProperty, LiveText.Resource("VideoFps"));
            RegisterRecorderPanel(_fpsSelector, BuildFpsSettings);
            strip.Children.Add(_fpsSelector);
            _record = ToolButton("RecordingRecordStop", null, async () =>
            {
                if (_controller.IsRecording || _controller.IsCountingDown) await _controller.StopAsync();
                else await _controller.StartAsync(this);
            });
            strip.Children.Add(_record);
            _recordModePanel = RegisterRecorderPanel(_record, BuildRecordingModeSettings);
            _pause = ToolButton("RecordingPauseContinue", "AnimationPause", () =>
            {
                _controller.TogglePause();
                return Task.CompletedTask;
            });
            strip.Children.Add(_pause);
            _audio = ToolButton("RecordingAudio", null, () => Task.CompletedTask);
            _audio.Content = ToolbarIcon.Create(_audio, "RecordingAudio");
            RegisterRecorderPanel(_audio, BuildAudioSettings);
            strip.Children.Add(_audio);
            _input = ToolButton("RecordingInputActions", "SettingsLocalInput", () => Task.CompletedTask);
            RegisterRecorderPanel(_input, BuildInputSettings);
            strip.Children.Add(_input);
            _camera = ToolButton("RecordingCamera", null, () => Task.CompletedTask);
            _camera.Content = ToolbarIcon.Create(_camera, "RecordingCamera");
            _camera.IsVisible = false;
            RegisterRecorderPanel(_camera, BuildCameraSettings);
            strip.Children.Add(_camera);
            strip.Children.Add(new Border { Width = 1, Background = AppTheme.Outline, Margin = new(5, 4) });
            strip.Children.Add(Annotations.Strip);
            strip.Children.Add(new Border { Width = 1, Background = AppTheme.Outline, Margin = new(5, 4) });
            var move = ToolButton("RecordingMoveRegion", "ToolbarMove", () => Task.CompletedTask);
            _move = move;
            var moveTitle = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
            moveTitle.Bind(TextBlock.TextProperty, LiveText.Resource("RecordingMoveRegion"));
            ToolTip.SetTip(move, new StackPanel { Spacing = 3, Children = { moveTitle, _regionInfo } });
            move.PointerPressed += (_, args) =>
            {
                if (!_controller.CanConfigure || !args.GetCurrentPoint(move).Properties.IsLeftButtonPressed) return;
                _moveOrigin = move.PointToScreen(args.GetPosition(move));
                _moveRegion = _controller.Selection.Region;
                args.Pointer.Capture(move);
                args.Handled = true;
            };
            move.PointerMoved += (_, args) =>
            {
                if (_moveOrigin is not { } origin) return;
                var point = move.PointToScreen(args.GetPosition(move));
                var region = _moveRegion with
                {
                    X = _moveRegion.X + point.X - origin.X, Y = _moveRegion.Y + point.Y - origin.Y,
                };
                if (Covers(region)) _controller.UpdateRegion(region);
                args.Handled = true;
            };
            move.PointerReleased += (_, args) =>
            {
                _moveOrigin = null;
                args.Pointer.Capture(null);
            };
            move.PointerCaptureLost += (_, _) => _moveOrigin = null;
            strip.Children.Add(move);
            strip.Children.Add(ToolButton("Close", "Cancel", _controller.CloseAsync));
            _rerecord = ToolButton("RecordingRerecord", null, _controller.RerecordAsync);
            _rerecord.Content = ToolbarIcon.Create(_rerecord, "RecordingRerecord");
            strip.Children.Add(_rerecord);
            var noticeLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            noticeLayer.Children.Add(_blockedToolNotice);
            noticeLayer.SizeChanged += (_, _) => PlaceBlockedRecordingToolNotice(noticeLayer);
            _blockedToolNotice.SizeChanged += (_, _) => PlaceBlockedRecordingToolNotice(noticeLayer);
            var toolbarLayer = new Grid { Children = { strip, noticeLayer } };
            var content = new StackPanel { Spacing = 4, Children = { toolbarLayer, _error } };
            Content = new Border
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                CornerRadius = new(8), Padding = new(6), Child = content,
            };
            _controller.Changed += Refresh;
            _controller.Completed += Finish;
            UiText.LanguageChanged += Refresh;
            Opened += (_, _) =>
            {
                _hasShown = true;
                _captureExcluded = RecordingRegionOverlay.Exclude(this);
                Refresh();
                _cameraInitialization = GuardAsync(async () =>
                {
                    await _controller.PrepareSelectedSourceAsync();
                    await LoadCameraDevicesAsync();
                });
            };
            SizeChanged += (_, _) => Reposition();
            ScalingChanged += (_, _) => InvalidateAnnotationDensity();
            Screens.Changed += RecordingScreensChanged;
            Closing += (sender, args) =>
            {
                if (_allowClose) return;
                HideRecorderPanels();
                Annotations.HideFlyouts();
                args.Cancel = true;
                if (!_controller.IsClosed) _ = GuardAsync(_controller.CloseAsync);
            };
            Closed += (_, _) =>
            {
                _closed = true;
                _nativePopupSubscription?.Dispose();
                _nativePopupSubscription = null;
                HideRecorderPanels();
                _controller.Changed -= Refresh;
                _controller.Completed -= Finish;
                UiText.LanguageChanged -= Refresh;
                _frame.LayoutChanged -= InvalidateAnnotationDensity;
                Screens.Changed -= RecordingScreensChanged;
                CloseCountdownWindow();
                _frame.Dispose();
                Annotations.CancelRequested -= CancelAnnotationContext;
                Annotations.BlockedToolClicked -= ShowBlockedRecordingToolNotice;
                _blockedToolNoticeTimer.Tick -= BlockedRecordingToolNoticeTick;
                HideBlockedRecordingToolNotice();
                Annotations.NativeExclusionFailed -= NativeAnnotationExclusionFailed;
                Annotations.Dispose();
                foreach (var panel in _recorderPanels) panel.Dispose();
                _recorderPanels.Clear();
                _controller.Dispose();
            };
            Refresh();
        }

        internal Action<IReadOnlyList<string>, PixelRect>? PublishOutput
        {
            set => _controller.PublishOutput = value;
        }

        internal bool CanAcceptSelection => _controller.CanConfigure;
        internal RecordingAnnotationLayer Annotations { get; }

        internal RecordingInputTrackState? InputTracks => _controller.InputTracks;
        internal RecordingCameraSession? CameraSession => _controller.CameraSession;

        private bool SourceExcludesRecorder => OperatingSystem.IsMacOS() || _controller.SourceIsWindowIsolated;

        private void CancelAnnotationContext()
        {
            _ = GuardAsync(_controller.CloseAsync);
        }

        internal Task CloseForShutdownAsync()
        {
            return _controller.CloseAsync();
        }

        internal void SetRecordingNativeSize(RecordingOptions options)
        {
            Dispatcher.UIThread.VerifyAccess();
            _recordingNativeSize = new(options.Width, options.Height);
            UpdateAnnotations(IsVisible);
        }

        internal void UpdateSelection(SelectionResult selection)
        {
            _controller.UpdateSelection(selection);
        }

        private async Task PrepareCaptureAsync(CancellationToken token)
        {
            _toolbarCaptureBeforeStart = null;
            await _cameraInitialization.WaitAsync(token);
            await _controller.EnsureCameraReadyAsync();
            HideRecorderPanels();
            Annotations.HideFlyouts();
            var bounds = new PixelRect(Position.X, Position.Y,
                Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling)),
                Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling)));
            await ConfigureToolbarCaptureAsync(bounds, token);
        }

        private bool Covers(PixelRect region)
        {
            return _controller.Selection.WindowSource || new DisplayTopology(Screens.All.Select(screen =>
                new PixelRect(screen.Bounds.X,
                    screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height))).Covers(region);
        }

        private Button ToolButton(string key, string? icon, Func<Task> action)
        {
            var button = new Button
            {
                Width = 30, Height = 30, MinWidth = 0, MinHeight = 0, Padding = new(5),
                Background = Brushes.Transparent, Foreground = AppTheme.Ink, BorderThickness = new(0),
                Focusable = false,
            };
            button.Classes.Add("tool");
            button.Bind(ToolTip.TipProperty, LiveText.Resource(key));
            button.Bind(AutomationProperties.NameProperty, LiveText.Resource(key));
            if (icon is not null) button.Content = ToolbarIcon.Create(button, ToolbarIcon.GetDefinition(icon));
            button.Click += async (_, _) => await GuardAsync(action);
            return button;
        }

        private async Task GuardAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException) when (_closed)
            {
            }
            catch (Exception error)
            {
                LogHub.GetLogger("Desktop.ScreenshotRecording").ZLogError(error, $"Recording toolbar action failed.");
                if (!_closed)
                {
                    _error.Text = error.Message;
                    _error.IsVisible = true;
                }
            }
        }

        private void Refresh()
        {
            if (_closed) return;
            if (!_controller.IsWorking) ResetToolbarCaptureTitle();
            if (!_controller.IsWorking) _recordingNativeSize = null;
            if (_controller.IsClosed || _controller.Reviewing)
            {
                HideBlockedRecordingToolNotice();
                CloseCountdownWindow();
                _frame.Update(this, _controller.Selection.Region, false, false);
                UpdateAnnotations(false);
                UpdateCameraPreview(false);
                Hide();
                return;
            }

            if (_captureHidden && !_controller.IsWorking) _captureHidden = false;
            if (_hasShown && !IsVisible && !_captureHidden) WindowActivation.Show(this);
            var elapsedSeconds = (long)(_controller.Progress?.ElapsedSeconds ?? 0);
            var remaining = _controller.CountdownRemaining ?? (!_controller.IsRecording ? _controller.DelaySeconds : 0);
            _elapsed.Text = remaining > 0
                ? $"-{remaining / 60}:{remaining % 60:00}"
                : $"{elapsedSeconds / 60:00}:{elapsedSeconds % 60:00}";
            _fps.Text = $"{_controller.Fps}FPS";
            var region = _controller.Selection.Region;
            _sourceViewport?.UpdateRegion(region);
            _regionInfo.Text = $"{region.X}, {region.Y}  {region.Width} × {region.Height} px";
            var stopping = _controller.IsRecording || _controller.IsCountingDown;
            if (_recordingVisual != stopping)
            {
                _recordingVisual = stopping;
                _record.Content =
                    new SvgIcon(
                        ToolbarIcon.GetDefinition(stopping ? "RecordingStopIndicator" : "RecordingStartIndicator"))
                    {
                        Width = stopping ? 12 : 14, Height = stopping ? 12 : 14, Foreground = AppTheme.Error,
                    };
            }

            _record.IsEnabled = stopping || _controller.CanConfigure;
            _pause.IsEnabled = _controller.CanPause;
            if (_pausedVisual != _controller.IsPaused)
            {
                _pausedVisual = _controller.IsPaused;
                _pause.Content = ToolbarIcon.Create(_pause,
                    ToolbarIcon.GetDefinition(_controller.IsPaused ? "AnimationPlay" : "AnimationPause"));
            }

            _fpsSelector.IsEnabled = _controller.CanConfigure;
            _delaySelector.IsEnabled = _controller.CanConfigure;
            _camera.IsEnabled = _controller is { IsClosed: false, Reviewing: false, IsPreparingSource: false };
            _audio.IsVisible = !stopping;
            _audio.IsEnabled = _controller.CanConfigure && AudioCaptureBackend.IsSupported;
            _input.IsVisible = !_controller.Quick;
            _input.IsEnabled = _controller.CanChangeKeyboardActions || _controller.CanChangeMouseActions;
            ToolTip.SetTip(_input,
                UiText.Get(_controller.InputUnavailableReason is null
                    ? "RecordingInputActions"
                    : "RecordingInputUnavailable"));
            _move.IsEnabled = _controller.CanConfigure;
            _recordModePanel.IsEnabled = _controller.CanConfigure;
            _rerecord.IsEnabled = _controller.IsRecording;
            _error.Text = _controller.Error;
            _error.IsVisible = _controller.Error is not null;
            var frameBrush = _controller.IsCountingDown || _controller.IsPaused
                ? WaitingFrameBrush
                : _controller.IsRecording
                    ? ActiveFrameBrush
                    : AppTheme.Accent;
            _frame.SetBrush(frameBrush);
            _sourceViewport?.SetFrameBrush(frameBrush);
            _frame.Update(this, region, _controller.CanConfigure, IsVisible);
            UpdateCountdownWindow();
            UpdateAnnotations(IsVisible);
            UpdateCameraPreview(IsVisible);
            Reposition();
        }

        private void UpdateAnnotations(bool visible)
        {
            if (_nativeExclusionSourceRevision != _controller.SourceRevision)
            {
                _nativeExclusionSourceRevision = _controller.SourceRevision;
                _nativeAnnotationCapability = LinuxCaptureExclusionCapability.Unavailable;
            }

            var supported = SourceExcludesRecorder ||
                            _nativeAnnotationCapability is LinuxCaptureExclusionCapability.SelectedWindowIsolated or
                                LinuxCaptureExclusionCapability.NativeCompositorExclusion ||
                            (!_annotationUnavailable && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041));
            Annotations.Strip.IsEnabled = supported;
            var region = _controller.Selection.Region;
            if (_annotationDensityRegion != region || _annotationDivisor != ScreenshotRecordingController.Divisor)
            {
                var provisional = new RecordingOptions(region, Divisor: ScreenshotRecordingController.Divisor)
                {
                    PixelDensity = NativeDisplayCatalog.Density(region),
                };
                _annotationProvisionalSize = new(provisional.Width, provisional.Height);
                _annotationDensityRegion = region;
                _annotationDivisor = ScreenshotRecordingController.Divisor;
            }

            var size = _recordingNativeSize ?? _annotationProvisionalSize;
            try
            {
                Annotations.UpdateGeometry(this, region, size, visible && (supported || _preparingNativeExclusion),
                    SourceExcludesRecorder);
            }
            catch (NotSupportedException error)
            {
                _annotationUnavailable = true;
                Annotations.Strip.IsEnabled = false;
                Annotations.HideFlyouts();
                LogHub.GetLogger("Desktop.ScreenshotRecording").ZLogWarning(error,
                    $"Live recording annotations are unavailable for the selected capture source.");
                Annotations.UpdateGeometry(this, region, size, false);
            }
        }

        private void InvalidateAnnotationDensity()
        {
            if (_closed) return;
            _annotationDensityRegion = null;
            Refresh();
        }

        private void RecordingScreensChanged(object? sender, EventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess()) InvalidateAnnotationDensity();
            else Dispatcher.UIThread.Post(InvalidateAnnotationDensity);
        }

        private PixelRect RecorderPlacementRegion()
        {
            if (_sourceViewport is not { IsVisible: true } sourceViewport) return _controller.Selection.Region;
            var coordinateScale = OperatingSystem.IsMacOS() ? 1 : sourceViewport.RenderScaling;
            return new(sourceViewport.Position.X, sourceViewport.Position.Y,
                Math.Max(1, (int)Math.Ceiling(sourceViewport.Bounds.Width * coordinateScale)),
                Math.Max(1, (int)Math.Ceiling(sourceViewport.Bounds.Height * coordinateScale)));
        }

        private void Reposition()
        {
            if (_closed || _controller.Reviewing) return;
            var region = RecorderPlacementRegion();
            _countdownWindow?.UpdateAnchor(region);
            var screen = Screens.ScreenFromPoint(new(region.X + region.Width / 2, region.Y + region.Height / 2)) ??
                         Screens.Primary;
            if (screen is null) return;
            var scale = screen.Scaling;
            var area = screen.WorkingArea;
            var viewport = new Size(area.Width / scale, area.Height / scale);
            var local = new Rect((region.X - area.X) / scale, (region.Y - area.Y) / scale,
                region.Width / scale, region.Height / scale);
            var position = ScreenshotToolbarLayout.Place(viewport, local, Bounds.Size);
            Position = new(area.X + (int)Math.Round(position.X * scale), area.Y + (int)Math.Round(position.Y * scale));
        }

        private void Finish()
        {
            if (_closed) return;
            _allowClose = true;
            Close();
        }
    }
}
