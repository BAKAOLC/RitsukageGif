using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    // One instance per independent timeline; all public operations are producer-thread operations.
    internal sealed class SceneFfmpegSource : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly List<Task> _loads = [];
        private CancellationTokenSource? _loading;
        private FfmpegInputSource? _source;
        private FfmpegFileSource? _file;
        private PresetMediaSource? _media;
        private PresetPlaybackState? _playback;
        private (byte[]? Pixels, int Width, int Height) _frozen;
        private int _generation;
        private bool _isLoading;
        private long _retryAt;
        private int _retryFailures;
        private FfmpegDependencyException? _dependencyError;
        private bool _disposed;

        public (byte[]? Pixels, int Width, int Height) Read(PresetMediaSource media,
            PresetPlaybackState? playback, double motionTime, double elapsedSeconds, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            var speed = playback?.Speed ?? media.Speed;
            if (!double.IsFinite(speed) || speed is < 0 or > 16)
                throw new ArgumentOutOfRangeException(nameof(playback));
            var paused = playback?.Paused == true || speed == 0;
            var position = playback is null
                ? media.Position + elapsedSeconds * media.Speed
                : playback.Position + (paused ? 0 : Math.Max(0, motionTime - playback.UpdatedAt) * speed);
            var changed = _media != media || _playback != playback;
            bool retry;
            lock (_gate)
            {
                if (!changed && _dependencyError is not null) throw _dependencyError;
                if (_source?.NeedsReconnect == true && _retryAt == 0) ScheduleRetry();
                retry = !_isLoading && _retryAt != 0 && Stopwatch.GetTimestamp() >= _retryAt;
            }

            if (changed || retry)
            {
                var initial = _media is null;
                _media = media;
                _playback = playback;
                _loading?.Cancel();
                _loading?.Dispose();
                var loading = CancellationTokenSource.CreateLinkedTokenSource(token);
                _loading = loading;
                var generation = ++_generation;
                lock (_gate)
                {
                    if (changed) _retryFailures = 0;
                    _retryAt = 0;
                    _isLoading = true;
                    _dependencyError = null;
                    _frozen = !changed && (playback?.HoldLast ?? media.HoldLast) && _source is not null
                        ? (_source.Capture(), _source.Width, _source.Height)
                        : default;
                    _source?.Dispose();
                    _source = null;
                    _file?.Dispose();
                    _file = null;
                }

                var options = new FfmpegInputOptions(media.File)
                {
                    Width = media.Width, Height = media.Height, Loop = playback?.Loop ?? media.Loop,
                    PlaybackRate = speed == 0 ? 1 : speed, PositionSeconds = position,
                    Live = media.Live, HoldLastFrame = playback?.HoldLast ?? media.HoldLast,
                    StaleAfterSeconds = media.StaleAfter
                };
                var load = LoadAsync(options, paused, generation, loading);
                _loads.RemoveAll(task => task.IsCompleted);
                _loads.Add(load);
                // Initial preparation is allowed to warm up. Subsequent seeks never stall other streams.
                if (initial) load.GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
            }

            lock (_gate)
            {
                if (_dependencyError is not null) throw _dependencyError;
                if (paused) return _frozen;
                if (_file is not null)
                    try
                    {
                        var pixels = _file.FrameAtAsync(position, playback?.Loop ?? media.Loop,
                            playback?.HoldLast ?? media.HoldLast, token).GetAwaiter().GetResult();
                        _frozen = (pixels, _file.Width, _file.Height);
                        return _frozen;
                    }
                    catch (Exception error) when (!token.IsCancellationRequested &&
                                                  error is IOException or InvalidOperationException
                                                      or OperationCanceledException)
                    {
                        _file.Dispose();
                        _file = null;
                        ScheduleRetry();
                        return (playback?.HoldLast ?? media.HoldLast) ? _frozen : default;
                    }

                if (_source is null) return _frozen;
                return (_source.Capture(), _source.Width, _source.Height);
            }
        }

        private async Task LoadAsync(FfmpegInputOptions options, bool paused, int generation,
            CancellationTokenSource loading)
        {
            FfmpegInputSource? source = null;
            FfmpegFileSource? file = null;
            try
            {
                if (!options.Live)
                {
                    file = await FfmpegFileSource.CreateAsync(options, loading.Token).ConfigureAwait(false);
                    var pixels = await file.FrameAtAsync(options.PositionSeconds, options.Loop,
                        options.HoldLastFrame, loading.Token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (_disposed || generation != _generation) return;
                        _retryFailures = 0;
                        _frozen = (pixels, file.Width, file.Height);
                        _file = file;
                        file = null;
                    }

                    return;
                }

                source = await FfmpegInputSource.CreateAsync(options, loading.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed || generation != _generation) return;
                    _retryFailures = 0;
                    if (paused)
                    {
                        _frozen = (source.Capture(), source.Width, source.Height);
                    }
                    else
                    {
                        _source = source;
                        source = null;
                    }
                }
            }
            catch (FfmpegDependencyException error)
            {
                lock (_gate)
                    if (!_disposed && generation == _generation)
                        _dependencyError = error;
            }
            catch (Exception error) when (error is OperationCanceledException or IOException
                                              or InvalidOperationException or TimeoutException
                                              or System.ComponentModel.Win32Exception
                                              or System.Text.Json.JsonException)
            {
                lock (_gate)
                    if (!_disposed && generation == _generation && !loading.IsCancellationRequested)
                        ScheduleRetry();
            }
            finally
            {
                source?.Dispose();
                file?.Dispose();
                lock (_gate)
                    if (generation == _generation)
                        _isLoading = false;
                // The current CTS remains alive for the producer's next Cancel call.
            }
        }

        private void ScheduleRetry()
        {
            var delay = Math.Min(30, 1 << Math.Min(_retryFailures++, 5));
            _retryAt = Stopwatch.GetTimestamp() + delay * Stopwatch.Frequency;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _loading?.Cancel();
                _source?.Dispose();
                _source = null;
                _file?.Dispose();
                _file = null;
                _frozen = default;
            }

            // Startup observes cancellation and releases any decoder it created before completion.
            Task.WhenAll(_loads).GetAwaiter().GetResult();
            _loading?.Dispose();
            _loads.Clear();
        }
    }
}
