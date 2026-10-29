using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSession(
        PixelRect initialRegion,
        ScrollCaptureAxis axis,
        PixelRect[] monitors,
        WindowTarget? target,
        HdrToneMapMode toneMap = HdrToneMapMode.HighlightCompression,
        FrozenScreenshotCursor? cursor = null)
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.ScrollCapture");
        private static long _sessionSequence;
        private readonly long _logSessionId = Interlocked.Increment(ref _sessionSequence);
        private readonly Lock _sync = new();
        private ScrollCaptureAxis _axis = axis;
        private int _captureEpoch;
        private bool _configured;
        private PixelRect? _firstRegion;
        private int _generation;
        private PixelRect _initialRegion = initialRegion;
        private long _lastPreview;
        private ScrollCaptureResult? _lastResult;
        private int _leadingInset, _trailingInset;
        private int _obstructed;
        private int _paused = 1;
        private int _position = axis == ScrollCaptureAxis.Vertical ? initialRegion.Y : initialRegion.X;
        private bool _previewDirty;
        private ScrollCaptureSession _stitching = new(initialRegion.Width, initialRegion.Height, new() { Axis = axis });
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
                _stitching = new(region.Width, region.Height, new() { Axis = _axis });
                _firstRegion = null;
                _configured = false;
                _leadingInset = _trailingInset = 0;
                _lastResult = null;
                _viewportMatched = false;
                _previewDirty = false;
                _captureEpoch++;
                Interlocked.Increment(ref _generation);
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
                    _stitching = new(_initialRegion.Width, _initialRegion.Height,
                        new() { Axis = _axis, LeadingInset = leading, TrailingInset = trailing });
                _leadingInset = leading;
                _trailingInset = trailing;
                _configured = true;
            }
        }

        public event Action<ScrollCaptureResult, ScrollCaptureImage?, bool, int>? Updated;
        public event Action<string>? Failed;

        public void Move(int position)
        {
            lock (_sync)
            {
                if (_position != position)
                {
                    _captureEpoch++;
                    _viewportMatched = false;
                    AutomaticEnabled = false;
                }

                Volatile.Write(ref _position, position);
            }
        }

        public async Task RunAsync(CancellationToken token)
        {
            var provider = CaptureFrame;
            var started = Stopwatch.GetTimestamp();
            long capturedFrames = 0, appendedFrames = 0, addedPixels = 0, unmatchedFrames = 0, failures = 0;
            var recovering = false;
            Logger.ZLogInformation(
                $"Scroll capture session started. SessionId={_logSessionId} Axis={_axis.ToString()} Width={_initialRegion.Width} Height={_initialRegion.Height} WindowSource={target is not null}");
            ScreenshotScrollingSource? capture = null;
            try
            {
                await Task.Delay(200, token).ConfigureAwait(false);
                while (!token.IsCancellationRequested)
                {
                    var generation = Generation;
                    var captureEpoch = 0;
                    try
                    {
                        PixelRect? pendingRegion = null;
                        lock (_sync)
                        {
                            generation = _generation;
                            if (Paused && _previewDirty && _lastResult is { } last) Publish(last, false);
                            if (!Paused && Volatile.Read(ref _obstructed) == 0)
                            {
                                var position = Volatile.Read(ref _position);
                                captureEpoch = _captureEpoch;
                                pendingRegion = _axis == ScrollCaptureAxis.Vertical
                                    ? _initialRegion with { Y = position }
                                    : _initialRegion with { X = position };
                            }
                        }

                        if (pendingRegion is { } region)
                        {
                            byte[] pixels;
                            if (provider is null)
                            {
                                capture ??= new(region, monitors, target, toneMap);
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
                                    var result = _stitching.TryAppend(pixels, region.X, region.Y, token);
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
                                            Logger.ZLogWarning(
                                                $"Scroll capture output limit reached. SessionId={_logSessionId} Width={result.Width} Height={result.Height}");
                                            AutomaticEnabled = false;
                                            Paused = true;
                                            break;
                                    }

                                    ObserveAutomatic(result, pixels, region, token);
                                    Publish(result,
                                        result.Status is ScrollCaptureStatus.Started or ScrollCaptureStatus.Appended);
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

                    await Task.Delay(100, token).ConfigureAwait(false);
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
            if (_previewDirty && (Paused || Environment.TickCount64 - _lastPreview >= 500))
            {
                preview = _stitching.ExportPreviewPixels(_previewMaximumWidth, _previewMaximumHeight);
                _lastPreview = Environment.TickCount64;
                _previewDirty = false;
            }

            Updated?.Invoke(result, preview, _stitching.CanUndo, _stitching.StartOffset);
        }

        public ScrollCaptureImage Export(CancellationToken token)
        {
            lock (_sync)
            {
                var image = _stitching.ExportPixels(token);
                Logger.ZLogInformation(
                    $"Scroll capture image exported. SessionId={_logSessionId} Width={image.Width} Height={image.Height} Bytes={image.Pixels.Length}");
                if (cursor is null || _firstRegion is not { } first) return image;
                var vertical = _axis == ScrollCaptureAxis.Vertical;
                var content = vertical
                    ? new PixelRect(first.X, first.Y + _leadingInset, first.Width,
                        first.Height - _leadingInset - _trailingInset)
                    : new PixelRect(first.X + _leadingInset, first.Y,
                        first.Width - _leadingInset - _trailingInset, first.Height);
                var output = vertical
                    ? new PixelRect(content.X, content.Y + _stitching.StartOffset, image.Width, image.Height)
                    : new PixelRect(content.X + _stitching.StartOffset, content.Y, image.Width, image.Height);
                var clipped = cursor.Bounds.Intersect(content).Intersect(output);
                if (clipped.IsEmpty) return image;
                var pixels = new byte[checked(clipped.Width * clipped.Height * 4)];
                var stride = checked(clipped.Width * 4);
                for (var row = 0; row < clipped.Height; row++)
                    image.Pixels.AsSpan(((clipped.Y - output.Y + row) * image.Width + clipped.X - output.X) * 4,
                        stride).CopyTo(pixels.AsSpan(row * stride, stride));
                cursor.Draw(pixels, clipped);
                for (var row = 0; row < clipped.Height; row++)
                    pixels.AsSpan(row * stride, stride).CopyTo(image.Pixels.AsSpan(
                        ((clipped.Y - output.Y + row) * image.Width + clipped.X - output.X) * 4, stride));
                return image;
            }
        }
    }
}
