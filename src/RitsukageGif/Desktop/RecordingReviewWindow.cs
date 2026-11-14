using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RitsukageGif.Desktop

{
    internal sealed partial class RecordingReviewWindow : Window

    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.RecordingReview");

        private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 2 };

        private readonly Button _audioButton = new();

        private readonly Stopwatch _clock = new();
        private readonly Button _closeButton = new();

        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly RecordingReviewDocument _document;
        private readonly Button _format = new();

        private readonly HdrPreviewHost _hdrPreview = new() { TransparentComposition = true };

        private readonly CancellationTokenSource _lifetime;

        private readonly Button _play = new();
        private readonly Image _preview = new() { Stretch = Stretch.Uniform };

        private readonly Button _speed = new() { Content = "1×", MinHeight = 28, MinWidth = 42 };

        private readonly TextBlock _status = new()
            { TextWrapping = TextWrapping.Wrap, Margin = new(4), IsVisible = false };

        private readonly TextBlock _time = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 88 };

        private readonly RecordingReviewTimeline _timeline = new();

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

        private readonly Popup _toolbar = new();

        private RecordingAudioPreview? _audio;

        private bool _autoExitAfterEncoding, _exitAfterExport;

        private WriteableBitmap? _bitmap;

        private long _displayedSecond = -1;
        private string _durationText = "";

        private CancellationTokenSource? _export;
        private RecordingInputEffects _inputEffects;

        private Task _loading = Task.CompletedTask, _frames = Task.CompletedTask, _command = Task.CompletedTask;

        private string _outputFormat;

        private double? _pendingFrame;
        private RecordingInputEffects _persistedInputEffects;
        private RecordingProgressEffects _persistedProgressEffects;

        private bool _playing, _closing, _audioFinished, _exported;

        private double _position, _playStart, _rate = 1;
        private RecordingProgressEffects _progressEffects;

        private RecordingReviewQuality _quality;

        private RecordingReviewResult _result;

        private int _seekRevision;


        private RecordingReviewSource? _source;
        private RecordingInputTraceDocument? _trace;

        private RecordingReviewWindow(Window owner, RecordingReviewDocument document, CancellationToken token)

        {
            _document = document;
            _inputEffects = document.InputEffects;
            _progressEffects = document.ProgressEffects;
            _persistedInputEffects = _inputEffects;
            _persistedProgressEffects = _progressEffects;
            _autoExitAfterEncoding = document.AutoExitAfterEncoding;

            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);

            _outputFormat = document.Format.ToString().ToLowerInvariant();
            _quality = document.Quality;

            Title = UiText.Get("RecordingReviewTitle");

            ShowInTaskbar = false;

            WindowDecorations = WindowDecorations.None;

            CanResize = false;

            Background = AppTheme.Workspace;

            Foreground = AppTheme.Ink;

            var screen = Screens.ScreenFromPoint(new(document.CaptureRegion.X + document.CaptureRegion.Width / 2,
                document.CaptureRegion.Y + document.CaptureRegion.Height / 2));
            var scale = OperatingSystem.IsMacOS() ? 1 : screen?.Scaling ?? owner.RenderScaling;
            Width = Math.Max(64, document.CaptureRegion.Width / scale);

            Height = Math.Max(48, document.CaptureRegion.Height / scale);

            Position = new(document.CaptureRegion.X, document.CaptureRegion.Y);

            WindowStartupLocation = WindowStartupLocation.Manual;

            Content = new Border

            {
                BorderBrush = AppTheme.Outline, BorderThickness = new(1),

                Child = new Grid { Children = { _preview, _hdrPreview } },
            };

            _hdrPreview.PresentationChanged += visible => _preview.Opacity = visible ? 0 : 1;
            _status.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBlock.TextProperty) _status.IsVisible = !string.IsNullOrEmpty(_status.Text);
            };
            BuildToolbar();

            SetActionsEnabled(false);

            _timer.Tick += Tick;

            _timeline.SeekRequested += position => Run(() => SeekAsync(position));

            _timeline.RangeChanged += () =>
                Run(() => SeekMediaAsync(_position));

            _preview.SizeChanged += (_, _) => RequestFrame(_position);

            ScalingChanged += (_, _) => RequestFrame(_position);

            Opened += (_, _) =>
            {
                _toolbar.IsOpen = true;
                _loading = LoadAsync();
            };

            Closed += (_, _) =>

            {
                _closing = true;

                _timer.Stop();

                _toolbar.IsOpen = false;

                foreach (var panel in _propertyPanels) panel.Dispose();
                _propertyPanels.Clear();

                _lifetime.Cancel();

                _export?.Cancel();

                _closed.TrySetResult();
            };

            AddHandler(KeyDownEvent, NavigationKeyPressed, RoutingStrategies.Tunnel);
            KeyDown += KeyPressed;
        }

        internal static async Task<RecordingReviewResult> ShowAsync(Window owner, RecordingReviewDocument document,
            CancellationToken token)

        {
            token.ThrowIfCancellationRequested();

            var window = new RecordingReviewWindow(owner, document, token);

            await using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));

            try

            {
                window.Show();

                await window._closed.Task;
            }

            finally

            {
                await window._loading;

                await window._command;
                await window._mixing;

                await window._frames;

                if (window._audio is not null) await window._audio.DisposeAsync();

                if (window._source is not null) await window._source.DisposeAsync();

                window._trace?.Dispose();
                window._hdrPreview.Dispose();
                window._bitmap?.Dispose();

                window._lifetime.Dispose();
            }

            return window._result == RecordingReviewResult.Rerecord
                ? RecordingReviewResult.Rerecord
                : window._exported
                    ? RecordingReviewResult.Exported
                    : RecordingReviewResult.Closed;
        }

        private async Task LoadAsync()

        {
            try

            {
                _source = _document.Colors == OutputColorMode.Both
                    ? await RecordingReviewPairedSource.OpenAsync(_document, ViewportPixels(), _lifetime.Token)
                    : await RecordingReviewSource.OpenAsync(_document.Paths[0], _document.Video, ViewportPixels(),
                        _lifetime.Token);

                _lifetime.Token.ThrowIfCancellationRequested();

                if (_source.HasAudio)

                {
                    var executable = FfmpegEncoder.FindExecutable() ??
                                     throw new IOException(UiText.Get("FfmpegMissing"));

                    _audio = await RecordingAudioPreview.OpenAsync(executable, _document.Paths[0],
                        _document.RecordedDuration.TotalSeconds, _lifetime.Token);
                }

                _trace = RecordingInputTraceDocument.TryOpenForMedia(_document.Paths[0]);
                if (_trace?.UnavailableReason is { Length: > 0 } reason)
                    Logger.ZLogWarning($"Recording input trace contains unavailable input sources: {reason}");
                _timeline.SetDuration(_document.RecordedDuration.TotalSeconds);
                _durationText = FormatPosition(_document.RecordedDuration.TotalSeconds);

                SetActionsEnabled(true);
                RefreshInputCapabilities();

                BuildAudioControls();

                _status.Text = _document.SourceLost ? UiText.Get("WindowSourceLostStopped") : "";

                _toolbar.IsOpen = true;

                RequestFrame(0);

                _timer.Start();

                await SetPlayingAsync(true);
            }

            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }

            catch (Exception error)

            {
                Logger.ZLogError(error, $"Recording review could not be opened.");

                if (!_closing) _status.Text = error.Message;
            }
        }

        private void Run(Func<Task> operation)

        {
            if (_closing || !_command.IsCompleted) return;

            _command = RunCoreAsync(operation);
        }

        private async Task RunCoreAsync(Func<Task> operation)

        {
            SetActionsEnabled(false);
            try
            {
                await operation();
            }

            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested ||
                                                     _export?.IsCancellationRequested == true)
            {
            }

            catch (Exception error)

            {
                Logger.ZLogError(error, $"Recording review operation failed.");

                if (!_closing) _status.Text = error.Message;
            }
            finally
            {
                if (!_closing) SetActionsEnabled(true);
            }
        }

        private PixelSize ViewportPixels()
        {
            return new(Math.Max(1, (int)Math.Ceiling(_preview.Bounds.Width * RenderScaling)),
                Math.Max(1, (int)Math.Ceiling(_preview.Bounds.Height * RenderScaling)));
        }
    }
}
