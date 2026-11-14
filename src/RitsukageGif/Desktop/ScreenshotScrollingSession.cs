using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSession
    {
        private const int MaximumStreamingLength = 2_000_000;
        private const int CaptureIntervalMilliseconds = 50;
        private const int PreviewIntervalMilliseconds = 100;
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.ScrollCapture");
        private static long _sessionSequence;
        private readonly FrozenScreenshotCursor? _cursor;
        private readonly double _density;
        private readonly long _logSessionId = Interlocked.Increment(ref _sessionSequence);
        private readonly PixelRect[] _monitors;
        private readonly bool _retainHdr;
        private readonly Lock _sync = new();
        private readonly WindowTarget? _target;
        private readonly HdrToneMapMode _toneMap;
        private ScrollCaptureAxis _axis;
        private int _captureEpoch;
        private bool _configured;
        private PixelRect? _firstRegion;
        private int _generation;
        private PixelRect _initialRegion;
        private long _lastPreview;
        private ScrollCaptureResult? _lastResult;
        private int _leadingInset, _trailingInset;
        private int _obstructed;
        private int _paused = 1;
        private int _position;
        private bool _previewDirty;
        private TaskCompletionSource? _stateChanged;

        private ScrollCaptureSession _stitching;

        internal ScreenshotScrollingSession(PixelRect initialRegion, ScrollCaptureAxis axis, PixelRect[] monitors,
            WindowTarget? target, HdrToneMapMode toneMap = HdrToneMapMode.HighlightCompression,
            FrozenScreenshotCursor? cursor = null, bool autoCropOnReverse = false, bool retainHdr = false,
            double? pixelDensity = null)
        {
            _monitors = monitors;
            _target = target;
            _toneMap = toneMap;
            _cursor = cursor;
            _retainHdr = retainHdr;
            _density = pixelDensity ?? NativeDisplayCatalog.Density(initialRegion);
            var options = new RecordingOptions(initialRegion, Divisor: 1) { PixelDensity = _density };
            options.Validate();
            _axis = axis;
            _previewMaximumHeight = axis == ScrollCaptureAxis.Vertical ? 1600 : 400;
            _previewMaximumWidth = axis == ScrollCaptureAxis.Vertical ? 400 : 1600;
            _initialRegion = initialRegion;
            _position = axis == ScrollCaptureAxis.Vertical ? initialRegion.Y : initialRegion.X;
            _stitching = CreateStitching(options.Width, options.Height, axis, autoCropOnReverse);
        }

        internal int Generation => Volatile.Read(ref _generation);


        public bool Paused
        {
            get => Volatile.Read(ref _paused) != 0;
            set
            {
                lock (_sync)
                {
                    if (Paused != value)
                    {
                        _captureEpoch++;
                        Logger.ZLogDebug(
                            $"Scroll capture pause changed. SessionId={_logSessionId} Paused={value} Generation={_generation}");
                        WakeCapture();
                    }

                    Volatile.Write(ref _paused, value ? 1 : 0);
                    _automatic?.Configure(_automaticEnabled, value);
                    NotifyAutomaticState();
                }
            }
        }

        public bool Obstructed
        {
            set
            {
                lock (_sync)
                {
                    var next = value ? 1 : 0;
                    if (_obstructed != next)
                    {
                        _captureEpoch++;
                        Logger.ZLogDebug(
                            $"Scroll capture obstruction changed. SessionId={_logSessionId} Obstructed={value}");
                    }

                    Volatile.Write(ref _obstructed, next);
                }
            }
        }

        internal void ConfigureAxis(ScrollCaptureAxis newAxis, PixelRect region)
        {
            lock (_sync)
            {
                Paused = true;
                _axis = newAxis;
                _initialRegion = region;
                _position = _axis == ScrollCaptureAxis.Vertical ? region.Y : region.X;
                ReplaceStitching(CreateStitching(PhysicalSize(region.Width), PhysicalSize(region.Height), newAxis,
                    AutoCropOnReverse));
                _firstRegion = null;
                _configured = false;
                _fileOnlyWarningResolved = false;
                _leadingInset = _trailingInset = 0;
                _lastResult = null;
                _viewportMatched = false;
                _previewDirty = false;
                _captureEpoch++;
                Interlocked.Increment(ref _generation);
                WakeCapture();
                Logger.ZLogInformation(
                    $"Scroll capture axis reset. SessionId={_logSessionId} Axis={_axis.ToString()} Width={region.Width} Height={region.Height} Generation={_generation}");
                AutomaticEnabled = false;
            }
        }

        public void ConfigureInsets(int leading, int trailing)
        {
            lock (_sync)
            {
                if (!Paused || _configured ||
                    (_axis == ScrollCaptureAxis.Vertical ? _stitching.Height : _stitching.Width) > 0)
                    throw new InvalidOperationException();
                if (leading != 0 || trailing != 0)
                    ReplaceStitching(CreateStitching(PhysicalSize(_initialRegion.Width),
                        PhysicalSize(_initialRegion.Height), _axis, AutoCropOnReverse, PhysicalOffset(leading),
                        PhysicalOffset(trailing)));
                _leadingInset = PhysicalOffset(leading);
                _trailingInset = PhysicalOffset(trailing);
                _configured = true;
            }
        }

        public event Action<ScrollCaptureResult, ScrollCaptureImage?, bool, int>? Updated;
        public event Action<string>? Failed;

        private ScrollCaptureSession CreateStitching(int width, int height, ScrollCaptureAxis axis,
            bool autoCropOnReverse, int leading = 0, int trailing = 0)
        {
            return new(width, height, new()
            {
                Axis = axis, LeadingInset = leading, TrailingInset = trailing, AutoCropOnReverse = autoCropOnReverse,
                RetainHdr = _retainHdr, UseDiskStorage = true, MaximumLength = MaximumStreamingLength,
            });
        }

        private void ReplaceStitching(ScrollCaptureSession replacement)
        {
            var previous = _stitching;
            _stitching = replacement;
            previous.Dispose();
        }

        internal void DisposeStitching()
        {
            lock (_sync) _stitching.Dispose();
        }

        internal bool SupportsStoppedResize(PixelRect region)
        {
            if (region.Width < 16 || region.Height < 16) return false;
            lock (_sync)
            {
                var width = PhysicalSize(region.Width);
                var height = PhysicalSize(region.Height);
                var length = _axis == ScrollCaptureAxis.Vertical ? height : width;
                var pixels = (long)width * height;
                return Paused && length <= 32768 && pixels <= 64 * 1024 * 1024 &&
                       pixels * (_retainHdr ? 33 : 17) + length * sizeof(double) <= 256L * 1024 * 1024;
            }
        }

        internal void Move(PixelRect region, bool rebase)
        {
            lock (_sync)
            {
                var position = _axis == ScrollCaptureAxis.Vertical ? region.Y : region.X;
                if (rebase) _initialRegion = region;
                if (rebase || _position != position)
                {
                    _captureEpoch++;
                    _viewportMatched = false;
                    _automatic?.ResetViewport();
                }

                Volatile.Write(ref _position, position);
            }
        }

        private void WakeCapture()
        {
            _stateChanged?.TrySetResult();
            _stateChanged = null;
        }

        public async Task RunAsync(CancellationToken token)
        {
            var provider = CaptureFrame;
            var started = Stopwatch.GetTimestamp();
            long capturedFrames = 0, appendedFrames = 0, addedPixels = 0, unmatchedFrames = 0, failures = 0;
            var recovering = false;
            Logger.ZLogInformation(
                $"Scroll capture session started. SessionId={_logSessionId} Axis={_axis.ToString()} Width={_initialRegion.Width} Height={_initialRegion.Height} WindowSource={_target is not null} RetainHdr={_retainHdr}");
            ScreenshotScrollingSource? capture = null;
            try
            {
                await Task.Delay(200, token).ConfigureAwait(false);
                while (!token.IsCancellationRequested)
                {
                    var frameStarted = Environment.TickCount64;
                    var generation = Generation;
                    var captureEpoch = 0;
                    try
                    {
                        PixelRect? pendingRegion = null;
                        Task? stateChanged = null;
                        lock (_sync)
                        {
                            generation = _generation;
                            if (Paused && _previewDirty && _lastResult is { } last) Publish(last, false);
                            if (Paused)
                            {
                                _stateChanged ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                                stateChanged = _stateChanged.Task;
                            }
                            else if (Volatile.Read(ref _obstructed) == 0)
                            {
                                var position = Volatile.Read(ref _position);
                                captureEpoch = _captureEpoch;
                                pendingRegion = _axis == ScrollCaptureAxis.Vertical
                                    ? _initialRegion with { Y = position }
                                    : _initialRegion with { X = position };
                            }
                        }

                        if (pendingRegion is null) capture?.Suspend();
                        if (stateChanged is not null)
                        {
                            await stateChanged.WaitAsync(token).ConfigureAwait(false);
                            continue;
                        }

                        if (pendingRegion is { } region)
                        {
                            CapturedFrameSet? frames = null;
                            byte[] pixels;
                            if (_retainHdr)
                            {
                                capture ??= CaptureFrameSet is null
                                    ? new(region, _monitors, _target, _toneMap, _density)
                                    : null;
                                frames = CaptureFrameSet is { } provide
                                    ? provide(region, token)
                                    : capture!.CaptureFrames(region, token);
                                pixels = CapturedFramePixels.ToStraightBgra(frames.Sdr, token);
                                frames = frames with
                                {
                                    Sdr = frames.Sdr with
                                    {
                                        Pixels = pixels, Stride = checked(PhysicalSize(region.Width) * 4),
                                        AlphaMode = CaptureAlphaMode.Straight,
                                    },
                                };
                            }
                            else if (provider is null)
                            {
                                capture ??= new(region, _monitors, _target, _toneMap, _density);
                                pixels = capture.Capture(region, token);
                            }
                            else
                            {
                                pixels = provider(region, token);
                            }

                            lock (_sync)
                                if (generation == _generation && captureEpoch == _captureEpoch && !Paused &&
                                    Volatile.Read(ref _obstructed) == 0)
                                {
                                    if (frames?.Hdr is { } firstHdr && _firstRegion is null &&
                                        !(firstHdr.ColorRegions.Count == 0
                                            ? firstHdr.ColorInfo.IsHdrSource
                                            : firstHdr.ColorRegions.Any(part => part.ColorInfo.IsHdrSource)))
                                        throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
                                    var previousSize = (_stitching.Width, _stitching.Height);
                                    var result = frames is null
                                        ? _stitching.TryAppend(pixels, PhysicalOffset(region.X - _initialRegion.X),
                                            PhysicalOffset(region.Y - _initialRegion.Y), token)
                                        : _stitching.TryAppend(frames, PhysicalOffset(region.X - _initialRegion.X),
                                            PhysicalOffset(region.Y - _initialRegion.Y), token);
                                    _viewportMatched = result.Status is ScrollCaptureStatus.Started or
                                        ScrollCaptureStatus.Appended or ScrollCaptureStatus.AlreadyCovered;
                                    capturedFrames++;
                                    if (recovering)
                                    {
                                        recovering = false;
                                        Logger.ZLogInformation(
                                            $"Scroll capture recovered. SessionId={_logSessionId} Generation={generation} Width={region.Width} Height={region.Height}");
                                    }

                                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                                    switch (result.Status)
                                    {
                                        case ScrollCaptureStatus.Appended:
                                            appendedFrames++;
                                            addedPixels += result.AddedPixels;
                                            break;
                                        case ScrollCaptureStatus.Unmatched:
                                            unmatchedFrames++;
                                            break;
                                        case ScrollCaptureStatus.Started:
                                            Logger.ZLogInformation(
                                                $"Scroll capture first frame accepted. SessionId={_logSessionId} Generation={generation} Width={region.Width} Height={region.Height}");
                                            _firstRegion = region;
                                            break;
                                        case ScrollCaptureStatus.LimitReached:
                                            if (_lastResult is not { Status: ScrollCaptureStatus.LimitReached })
                                                Logger.ZLogWarning(
                                                    $"Scroll capture output limit reached. SessionId={_logSessionId} Width={result.Width} Height={result.Height}");
                                            if (AutomaticEnabled) AutomaticEnabled = false;
                                            break;
                                    }

                                    ObserveAutomatic(result, region, token);
                                    if ((_previewDirty && Environment.TickCount64 - _lastPreview >=
                                            PreviewIntervalMilliseconds) ||
                                        result.Status != ScrollCaptureStatus.LimitReached ||
                                        _lastResult is not { Status: ScrollCaptureStatus.LimitReached })
                                        Publish(result, previousSize != (_stitching.Width, _stitching.Height) ||
                                                        result.Status is ScrollCaptureStatus.Started
                                                            or ScrollCaptureStatus.Appended);
                                }
                        }
                    }
                    catch (Exception error)
                    {
                        token.ThrowIfCancellationRequested();
                        lock (_sync)
                            if (generation == _generation && captureEpoch == _captureEpoch)
                            {
                                failures++;
                                _viewportMatched = false;
                                recovering = true;
                                Logger.ZLogWarning(error,
                                    $"Scroll capture failed and paused. SessionId={_logSessionId} Generation={generation} CapturedFrames={capturedFrames}");
                                AutomaticEnabled = false;
                                Paused = true;
                                Failed?.Invoke(error.Message);
                            }

                        var failedCapture = capture;
                        capture = null;
                        try
                        {
                            failedCapture?.Dispose();
                        }
                        catch (Exception cleanupError)
                        {
                            Logger.ZLogError(cleanupError,
                                $"Scroll capture backend cleanup failed. SessionId={_logSessionId}");
                        }
                    }

                    var delay = CaptureIntervalMilliseconds - (Environment.TickCount64 - frameStarted);
                    if (delay > 0) await Task.Delay((int)delay, token).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    capture?.Dispose();
                }
                finally
                {
                    Logger.ZLogInformation(
                        $"Scroll capture session stopped. SessionId={_logSessionId} CapturedFrames={capturedFrames} AppendedFrames={appendedFrames} AddedPixels={addedPixels} UnmatchedFrames={unmatchedFrames} Failures={failures} Canceled={token.IsCancellationRequested} ElapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds}");
                }
            }
        }

        private void Publish(ScrollCaptureResult result, bool changed)
        {
            _lastResult = result;
            ScrollCaptureImage? preview = null;
            _previewDirty |= changed;
            if (_previewDirty && (Paused || Environment.TickCount64 - _lastPreview >= PreviewIntervalMilliseconds))
            {
                preview = _stitching.ExportPreviewPixels(_previewMaximumWidth, _previewMaximumHeight);
                _lastPreview = Environment.TickCount64;
                _previewDirty = false;
            }

            Updated?.Invoke(result, preview, _stitching.CanUndo, _stitching.StartOffset);
        }

        private ScrollCaptureImage Export(CancellationToken token)
        {
            lock (_sync)
            {
                var image = _stitching.ExportPixels(token);
                Logger.ZLogInformation(
                    $"Scroll capture image exported. SessionId={_logSessionId} Width={image.Width} Height={image.Height} CombinedBytes={image.Pixels.LongLength + (image.Hdr?.Pixels.Length ?? 0)} HasHdr={image.Hdr is not null}");
                if (CursorOverlay(image.Width, image.Height) is not { } overlay) return image;
                BlendCursor(image.Pixels, image.Width, overlay.Area, overlay.Pixels, token);
                return image;
            }
        }

        private int PhysicalSize(int value)
        {
            return checked((int)Math.Ceiling(value * _density));
        }

        private int PhysicalOffset(int value)
        {
            return checked((int)Math.Round(value * _density));
        }
    }
}
