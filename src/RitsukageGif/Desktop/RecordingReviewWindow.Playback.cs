using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop

{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecordingReviewWindow

    {
        private readonly Queue<Key> _navigationKeys = new();

        private double PlaybackStart => RecordingReviewSampling.MediaPosition(_document, _timeline.RangeStart);

        private double PlaybackEnd => Math.Min(_document.RecordedDuration.TotalSeconds,
            RecordingReviewSampling.MediaPosition(_document, _timeline.RangeEnd) + 1d / _document.FramesPerSecond);

        private async Task SetPlayingAsync(bool playing)

        {
            if (_source is null) return;

            if (!playing)

            {
                _position = CurrentPosition();

                _playing = false;

                _clock.Stop();

                if (_audio is not null) await _audio.PauseAsync();
            }

            else

            {
                if (_position >= PlaybackEnd) _position = PlaybackStart;

                _audioFinished = false;
                if (_audio is { HasAudio: true })
                    try
                    {
                        await _audio.PlayAsync(_position, _rate, _lifetime.Token);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        AudioFailed(error);
                    }

                _playStart = _position;

                _clock.Restart();

                _playing = true;
            }

            _play.Content =
                ToolbarIcon.Create(_play, ToolbarIcon.GetDefinition(_playing ? "AnimationPause" : "AnimationPlay"));

            RequestFrame(_position);
        }

        private double CurrentPosition()

        {
            if (!_playing) return _position;

            if (_audio is { HasAudio: true } && !_audioFinished)

            {
                var buffering = _audio.IsBuffering;
                if (_audioBuffering != buffering)
                {
                    _audioBuffering = buffering;
                    Logger.ZLogDebug($"Recording audio preview buffering state changed. Buffering={buffering}");
                }

                var position = _audio.Position;

                if (!_audio.Completion.IsCompleted || !_mixing.IsCompleted)
                    return Math.Clamp(position, PlaybackStart, PlaybackEnd);

                _audioFinished = true;

                if (_audio.Failure is { } error) _status.Text = error;

                _playStart = position;

                _clock.Restart();
            }

            return Math.Clamp(_playStart + _clock.Elapsed.TotalSeconds * _rate, PlaybackStart, PlaybackEnd);
        }

        private void Tick(object? sender, EventArgs args)

        {
            if (!_playing || _closing) return;

            var position = CurrentPosition();

            RequestFrame(position);

            if (position >= PlaybackEnd) Run(() => SetPlayingAsync(false));
        }

        private Task SeekAsync(double position)

        {
            return SeekMediaAsync(RecordingReviewSampling.MediaPosition(_document,
                Math.Clamp(position, _timeline.RangeStart, _timeline.RangeEnd)));
        }

        private async Task SeekMediaAsync(double position)

        {
            if (_source is null) return;

            _seekRevision++;

            _position = Math.Clamp(position, PlaybackStart,
                RecordingReviewSampling.MediaPosition(_document, _timeline.RangeEnd));

            _playStart = _position;

            _clock.Restart();

            _audioFinished = false;
            if (_audio is not null)
                try
                {
                    await _audio.SeekAsync(_position, _lifetime.Token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    AudioFailed(error);
                }

            RequestFrame(_position);
        }

        private void RequestFrame(double position)

        {
            if (_source is null || _closing) return;

            _pendingFrame = position;

            if (_frames.IsCompleted) _frames = LoadFramesAsync();
        }

        private async Task LoadFramesAsync()

        {
            try

            {
                while (_pendingFrame is { } position && !_closing)

                {
                    _pendingFrame = null;

                    var revision = _seekRevision;

                    var source = _source!;

                    await source.SetViewportAsync(ViewportPixels(), _lifetime.Token);

                    var frameIndex = RecordingReviewSampling.IndexAtTime(_document, position);
                    var mediaPosition = frameIndex / (double)_document.FramesPerSecond;
                    using var decoded = await source.FrameAtAsync(mediaPosition, _lifetime.Token);
                    var inputEffects = _inputEffects;
                    var progressEffects = _progressEffects;
                    var range = RecordingReviewSampling.Range(_document, _timeline.RangeStart, _timeline.RangeEnd);
                    var rate = _rate;
                    using var frame = await Task.Run(() => RenderEffects(decoded, mediaPosition, frameIndex,
                        inputEffects,
                        progressEffects, range, rate, source.OriginalWidth), _lifetime.Token);

                    _lifetime.Token.ThrowIfCancellationRequested();

                    if (revision != _seekRevision) continue;

                    if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width ||
                        _bitmap.PixelSize.Height != frame.Height)

                    {
                        var next = new WriteableBitmap(new(frame.Width, frame.Height), new(96, 96),
                            PixelFormat.Bgra8888, AlphaFormat.Premul);

                        var previous = _bitmap;

                        _bitmap = next;

                        _preview.Source = next;

                        previous?.Dispose();
                    }

                    using (var buffer = _bitmap.Lock())

                        for (var row = 0; row < frame.Height; row++)

                            Marshal.Copy(frame.Pixels, row * frame.Width * 4, buffer.Address + row * buffer.RowBytes,
                                frame.Width * 4);

                    _hdrPreview.SetFrame(frame);
                    _preview.InvalidateVisual();

                    var displayPosition = RecordingReviewSampling.DisplayPosition(_document, mediaPosition);
                    _timeline.SetPosition(displayPosition);

                    var second = (long)displayPosition;
                    if (_displayedSecond != second)
                    {
                        _displayedSecond = second;
                        _time.Text = FormatPosition(displayPosition) + "/" + _durationText;
                    }
                }
            }

            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }

            catch (Exception error)

            {
                Logger.ZLogError(error, $"Recording preview frame failed.");

                if (!_closing) _status.Text = error.Message;
            }
        }

        private PreviewFrame RenderEffects(PreviewFrame decoded, double position, long frameIndex,
            RecordingInputEffects inputEffects,
            RecordingProgressEffects progressEffects, RecordingReviewFrameRange range, double rate, int originalWidth)
        {
            return RecordingEffectsComposition.Render(decoded, TimeSpan.FromSeconds(position), _trace, inputEffects,
                frameIndex, range, rate, originalWidth, progressEffects, _document.FramesPerSecond, _lifetime.Token);
        }

        private static string FormatPosition(double seconds)
        {
            var whole = (long)Math.Max(0, seconds);
            return (whole / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                   (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private async Task StepFrameAsync(int direction)
        {
            if (_playing) await SetPlayingAsync(false);
            var index = RecordingReviewSampling.IndexAtTime(_document, _position);
            await SeekMediaAsync((index + direction) / (double)_document.FramesPerSecond);
        }

        private async Task JumpToBoundaryAsync(double position)
        {
            if (_playing) await SetPlayingAsync(false);
            await SeekAsync(position);
        }

        private void NavigationKeyPressed(object? sender, KeyEventArgs args)
        {
            if (args.Handled || _export is not null || _source is null || args.KeyModifiers != KeyModifiers.None)
                return;
            if (args.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) return;
            args.Handled = true;
            if (_closing) return;
            _navigationKeys.Enqueue(args.Key);
            if (_navigationKeys.Count == 1) _command = RunNavigationAsync(_command);
        }

        private async Task RunNavigationAsync(Task preceding)
        {
            await preceding;
            try
            {
                while (!_closing && _export is null && _navigationKeys.TryPeek(out var key))
                {
                    await RunCoreAsync(() => key switch
                    {
                        Key.Left => StepFrameAsync(-1),
                        Key.Right => StepFrameAsync(1),
                        Key.Home => JumpToBoundaryAsync(_timeline.RangeStart),
                        _ => JumpToBoundaryAsync(_timeline.RangeEnd),
                    });
                    _navigationKeys.Dequeue();
                }
            }
            finally
            {
                _navigationKeys.Clear();
            }
        }

        private void KeyPressed(object? sender, KeyEventArgs args)

        {
            if (args.Handled || _export is not null) return;
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                Close();
                return;
            }

            if (_source is null) return;

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (args.Key)

            {
                case Key.S when args.KeyModifiers == KeyModifiers.Control: Run(() => SaveAsync(false)); break;

                case Key.S
                    when args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                    Run(() => SaveAsync(true)); break;

                case Key.C when args.KeyModifiers == KeyModifiers.Control: Run(CopyAsync); break;

                case Key.Space: Run(() => SetPlayingAsync(!_playing)); break;

                case Key.Escape: Close(); break;

                default: return;
            }

            args.Handled = true;
        }
    }
}
