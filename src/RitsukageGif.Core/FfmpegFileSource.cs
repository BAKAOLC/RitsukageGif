using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;

namespace RitsukageGif.Core
{
    // A pull-driven file timeline. Backpressure bounds decoded memory independently of file duration.
    public sealed class FfmpegFileSource : IDisposable
    {
        private readonly string _executable;
        private readonly string _input;
        private readonly double _fallbackDuration;
        private readonly double _startTime;
        private readonly double _seekBase;
        private readonly double _estimatedDuration;
        private double _decodeStart;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly SemaphoreSlim _access = new(1);
        private CancellationTokenSource _decoderStop = null!;
        private Channel<FfmpegVideoFrame> _frames = null!;
        private Task _reader = Task.CompletedTask;
        private FfmpegVideoFrame? _current;
        private FfmpegVideoFrame? _next;
        private bool _ended;
        private double _duration;
        private int _disposed;

        private FfmpegFileSource(string executable, string input, int width, int height, double fallbackDuration,
            double startTime, double seekBase, double estimatedDuration)
        {
            _executable = executable;
            _input = input;
            Width = width;
            Height = height;
            _fallbackDuration = fallbackDuration;
            _startTime = startTime;
            _seekBase = seekBase;
            _estimatedDuration = estimatedDuration;
            StartDecoder();
        }

        public int Width { get; }
        public int Height { get; }

        public static async Task<FfmpegFileSource> CreateAsync(FfmpegInputOptions options,
            CancellationToken token = default)
        {
            var executable = FfmpegEncoder.FindExecutable()
                             ?? throw new FfmpegDependencyException(UiText.Get("FfmpegMissing"));
            var probe = Path.Combine(Path.GetDirectoryName(executable)!,
                OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            if (!File.Exists(probe)) throw new FfmpegDependencyException(UiText.Get("FfprobeMissing"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var process = new Process();
            process.StartInfo = FfmpegInputSource.StartInfo(probe);
            foreach (var argument in new[]
                     {
                         "-v", "error", "-select_streams", "v:0", "-show_entries",
                         "stream=width,height,avg_frame_rate,start_time,duration:format=start_time,duration", "-of",
                         "json", "-i", options.Input
                     }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            await using var registration =
                timeout.Token.Register(static value => FfmpegInputSource.Kill((Process)value!), process);
            var errors = FfmpegInputSource.DrainAsync(process.StandardError, timeout.Token);
            try
            {
                var output = await FfmpegInputSource.DrainAsync(process.StandardOutput, timeout.Token)
                    .ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                var detail = await errors.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new IOException(detail);
                using var document = JsonDocument.Parse(output);
                var streams = document.RootElement.GetProperty("streams");
                if (streams.GetArrayLength() == 0) throw new InvalidDataException();
                var stream = streams[0];
                var width = options.Width == 0 ? stream.GetProperty("width").GetInt32() : options.Width;
                var height = options.Height == 0 ? stream.GetProperty("height").GetInt32() : options.Height;
                if (width is <= 0 or > 16384 || height is <= 0 or > 16384 || (long)width * height > 33554432)
                    throw new ArgumentOutOfRangeException(nameof(options));
                var fallback = 0.04;
                if (stream.TryGetProperty("avg_frame_rate", out var rate))
                {
                    var parts = (rate.GetString() ?? "").Split('/');
                    if (parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator)
                                          && double.TryParse(parts[1], CultureInfo.InvariantCulture,
                                              out var denominator)
                                          && numerator > 0 && denominator > 0) fallback = denominator / numerator;
                }

                var startTime = JsonNumber(stream, "start_time");
                document.RootElement.TryGetProperty("format", out var format);
                var seekBase = startTime - JsonNumber(format, "start_time");
                var duration = JsonNumber(stream, "duration");
                if (duration <= 0) duration = Math.Max(0, JsonNumber(format, "duration") - Math.Max(0, startTime));
                return new FfmpegFileSource(executable, options.Input, width, height, fallback, startTime, seekBase,
                    duration);
            }
            finally
            {
                FfmpegInputSource.Kill(process);
                try
                {
                    await errors.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        public async Task<byte[]?> FrameAtAsync(double position, bool loop, bool holdLast,
            CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (!double.IsFinite(position) || position < 0) throw new ArgumentOutOfRangeException(nameof(position));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await _access.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                if (loop && _duration > 0) position %= _duration;
                if (!loop && _ended && position >= _duration)
                    return holdLast && _current is not null ? (byte[])_current.Pixels.Clone() : null;
                if ((_current is not null && position < _current.Time)
                    || position - (_current?.Time ?? 0) > 5)
                {
                    var end = _duration > 0 ? _duration : _estimatedDuration;
                    var seek = end > 0 ? Math.Min(position, Math.Max(0, end - 1)) : position;
                    await RestartAsync(seek).ConfigureAwait(false);
                }

                while (true)
                {
                    _current ??= await ReadFrameAsync(timeout.Token).ConfigureAwait(false);
                    if (_current is null)
                    {
                        if (_decodeStart <= 0) return null;
                        await RestartAsync(0).ConfigureAwait(false);
                        continue;
                    }

                    if (_current.Time > position + 0.0000001 && _decodeStart > 0)
                    {
                        await RestartAsync(0).ConfigureAwait(false);
                        continue;
                    }

                    _next ??= await ReadFrameAsync(timeout.Token).ConfigureAwait(false);
                    while (_next is not null && _next.Time <= position + 0.0000001)
                    {
                        _current = _next;
                        _next = await ReadFrameAsync(timeout.Token).ConfigureAwait(false);
                    }

                    if (!_ended || position < _duration) return (byte[])_current.Pixels.Clone();
                    if (!loop) return holdLast ? (byte[])_current.Pixels.Clone() : null;
                    position %= _duration;
                    await RestartAsync(position).ConfigureAwait(false);
                }
            }
            finally
            {
                _access.Release();
            }
        }

        private async Task<FfmpegVideoFrame?> ReadFrameAsync(CancellationToken token)
        {
            if (_ended) return null;
            if (await _frames.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                return await _frames.Reader.ReadAsync(token).ConfigureAwait(false);
            _ended = true;
            if (_current is not null)
                _duration = _current.Time + (_current.Duration > 0 ? _current.Duration
                    : _estimatedDuration > _current.Time ? _estimatedDuration - _current.Time : _fallbackDuration);
            return null;
        }

        private void StartDecoder(double position = 0)
        {
            _decoderStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _frames = Channel.CreateBounded<FfmpegVideoFrame>(new BoundedChannelOptions(1)
                { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            _decodeStart = position;
            var writer = _frames.Writer;
            var token = _decoderStop.Token;
            _reader = DecodeAsync(writer, position, token);
        }

        private async Task RestartAsync(double position)
        {
            await _decoderStop.CancelAsync().ConfigureAwait(false);
            await _reader.ConfigureAwait(false);
            _decoderStop.Dispose();
            _current = null;
            _next = null;
            _ended = false;
            StartDecoder(position);
        }

        private async Task DecodeAsync(ChannelWriter<FfmpegVideoFrame> frames, double position, CancellationToken token)
        {
            var times = Channel.CreateBounded<(double Time, double Duration)>(16);
            using var process = new Process();
            process.StartInfo = FfmpegInputSource.StartInfo(_executable);
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-nostats", "-loglevel", "info", "-nostdin", "-copyts",
                         "-noaccurate_seek", "-ss",
                         Math.Max(0, position + _seekBase).ToString("R", CultureInfo.InvariantCulture), "-i", _input,
                         "-map", "0:v:0", "-an", "-sn", "-dn", "-vf",
                         $"scale={Width}:{Height},setpts=PTS-({_startTime.ToString("R", CultureInfo.InvariantCulture)})/TB,showinfo=checksum=0",
                         "-fps_mode", "passthrough", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"
                     })
                process.StartInfo.ArgumentList.Add(argument);
            using var parsingStop = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<string>? errors = null;
            try
            {
                token.ThrowIfCancellationRequested();
                process.Start();
                await using var registration =
                    token.Register(static value => FfmpegInputSource.Kill((Process)value!), process);
                errors = ReadTimesAsync(process.StandardError, times.Writer, parsingStop.Token);
                while (true)
                {
                    var pixels = new byte[checked(Width * Height * 4)];
                    var read = 0;
                    while (read < pixels.Length)
                    {
                        var count = await process.StandardOutput.BaseStream.ReadAsync(pixels.AsMemory(read), token)
                            .ConfigureAwait(false);
                        if (count == 0) break;
                        read += count;
                    }

                    if (read == 0) break;
                    if (read != pixels.Length) throw new EndOfStreamException();
                    var timing = await times.Reader.ReadAsync(token).ConfigureAwait(false);
                    await frames.WriteAsync(new FfmpegVideoFrame(timing.Time, timing.Duration, pixels), token)
                        .ConfigureAwait(false);
                }

                await process.WaitForExitAsync(token).ConfigureAwait(false);
                var detail = await errors.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new IOException(detail);
                if (times.Reader.TryRead(out _)) throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                frames.TryComplete();
            }
            catch (Exception error)
            {
                frames.TryComplete(error);
            }
            finally
            {
                await parsingStop.CancelAsync().ConfigureAwait(false);
                FfmpegInputSource.Kill(process);
                if (errors is not null)
                    try
                    {
                        await errors.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
            }
        }

        private static async Task<string> ReadTimesAsync(StreamReader reader,
            ChannelWriter<(double Time, double Duration)> times, CancellationToken token)
        {
            var detail = new System.Text.StringBuilder();
            try
            {
                var state = (Frame: 0, TimeBase: 0d);
                var buffer = new char[1024];
                var line = new System.Text.StringBuilder();
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                    for (var index = 0; index < count; index++)
                        if (buffer[index] == '\n')
                        {
                            state = await ParseLineAsync(line.ToString(), state, times, detail, token)
                                .ConfigureAwait(false);
                            line.Clear();
                        }
                        else if (line.Length < 8192)
                        {
                            line.Append(buffer[index]);
                        }

                if (line.Length > 0)
                    await ParseLineAsync(line.ToString(), state, times, detail, token)
                        .ConfigureAwait(false);
                times.TryComplete();
                return detail.ToString();
            }
            catch (Exception error)
            {
                times.TryComplete(error);
                throw;
            }
        }

        private static async Task<(int Frame, double TimeBase)> ParseLineAsync(string line,
            (int Frame, double TimeBase) state,
            ChannelWriter<(double Time, double Duration)> times,
            System.Text.StringBuilder detail, CancellationToken token)
        {
            const string timeBaseKey = "config in time_base: ";
            var timeBaseIndex = line.IndexOf(timeBaseKey, StringComparison.Ordinal);
            if (timeBaseIndex >= 0)
            {
                var fraction = line.AsSpan(timeBaseIndex + timeBaseKey.Length);
                var comma = fraction.IndexOf(',');
                if (comma >= 0) fraction = fraction[..comma];
                var slash = fraction.IndexOf('/');
                if (slash > 0 && double.TryParse(fraction[..slash], CultureInfo.InvariantCulture, out var numerator)
                              && double.TryParse(fraction[(slash + 1)..], CultureInfo.InvariantCulture,
                                  out var denominator)
                              && numerator > 0 && denominator > 0) state.TimeBase = numerator / denominator;
                return state;
            }

            if (line.Contains("Parsed_showinfo_", StringComparison.Ordinal) &&
                TryValue(line, "pts_time:", out var time))
            {
                if (!TryInteger(line, " n:", out var number) || number != state.Frame)
                    throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                TryValue(line, "duration_time:", out var duration);
                // pts_time is rounded for human-readable logs; integer PTS preserves frame boundaries in long files.
                if (state.TimeBase > 0)
                {
                    if (TryValue(line, " pts:", out var pts)) time = pts * state.TimeBase;
                    if (TryValue(line, " duration:", out var ticks)) duration = ticks * state.TimeBase;
                }

                await times.WriteAsync((time, duration), token).ConfigureAwait(false);
                state.Frame++;
            }
            else
            {
                detail.AppendLine(line);
                if (detail.Length > 8192) detail.Remove(0, detail.Length - 8192);
            }

            return state;
        }

        private static double JsonNumber(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
                                                             && double.TryParse(value.ToString(), NumberStyles.Float,
                                                                 CultureInfo.InvariantCulture, out var number)
                                                             && double.IsFinite(number)
                ? number
                : 0;
        }

        private static bool TryInteger(string line, string key, out long value)
        {
            value = 0;
            var index = line.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) return false;
            var text = line.AsSpan(index + key.Length).TrimStart();
            var end = text.IndexOf(' ');
            if (end >= 0) text = text[..end];
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryValue(string line, string key, out double value)
        {
            value = 0;
            var index = line.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) return false;
            var text = line.AsSpan(index + key.Length).TrimStart();
            var end = text.IndexOf(' ');
            if (end >= 0) text = text[..end];
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   double.IsFinite(value);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _lifetime.Cancel();
            _access.Wait();
            _reader.GetAwaiter().GetResult();
            _decoderStop.Dispose();
            _lifetime.Dispose();
            _access.Dispose();
        }
    }
}
