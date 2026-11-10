using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingAudioPreview : IAsyncDisposable
    {
        private readonly SemaphoreSlim _access = new(1);
        private readonly double _duration;
        private readonly string _executable;
        private readonly string _path;
        private bool _disposed;
        private string? _failure;
        private double _position;
        private double _rate = 1;
        private RecordingAudioPreviewSession? _session;
        private IReadOnlyList<RecordingAudioTrackMix> _mix;

        private RecordingAudioPreview(string executable, string path, double duration,
            IReadOnlyList<RecordingAudioTrack> tracks)
        {
            _executable = executable;
            _path = path;
            _duration = duration;
            Tracks = tracks;
            _mix = Array.AsReadOnly<RecordingAudioTrackMix>(
                [.. tracks.Select(track => new RecordingAudioTrackMix(track.StreamIndex, false, 1))]);
        }

        public bool HasAudio => Tracks.Count > 0;
        internal IReadOnlyList<RecordingAudioTrack> Tracks { get; }
        internal IReadOnlyList<RecordingAudioTrackMix> Mix => Volatile.Read(ref _mix);
        internal bool HasSystemAudio => Tracks.Any(track => track.Kind == VideoAudioTrackKind.SystemAudio);
        internal bool HasMicrophone => Tracks.Any(track => track.Kind == VideoAudioTrackKind.Microphone);
        private bool IsPlaying => _session is { Completion.IsCompleted: false };
        public bool IsBuffering => _session is { IsBuffering: true, Completion.IsCompleted: false };
        public double Position => Math.Min(_duration, _session?.Position ?? _position);
        public string? Failure => _session?.Failure ?? _failure;
        public Task Completion => _session?.Completion ?? Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            await _access.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                _disposed = true;
                await StopCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _access.Release();
            }
        }

        public static async Task<RecordingAudioPreview> OpenAsync(string ffmpegPath, string path, double duration,
            CancellationToken token = default)
        {
            if (!double.IsFinite(duration) || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            var probe = Path.Combine(Path.GetDirectoryName(ffmpegPath)!,
                OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var process = new Process();
            process.StartInfo = StartInfo(probe);
            foreach (var argument in new[]
                     {
                         "-v", "error", "-select_streams", "a", "-show_entries",
                         "stream=index:stream_tags=rg_source_kind", "-of", "json", path,
                     })
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            // ReSharper disable once AccessToDisposedClosure
            await using var registration = timeout.Token.Register(() => Kill(process));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = ReadErrorsAsync(process.StandardError, timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                var detail = await errors.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new IOException($"Audio probe failed: {detail}");
                using var document = JsonDocument.Parse(await output.ConfigureAwait(false));
                var tracks = document.RootElement.GetProperty("streams").EnumerateArray().Select(stream =>
                {
                    var kind = stream.TryGetProperty("tags", out var tags)
                        ? tags.EnumerateObject().FirstOrDefault(tag =>
                            tag.Name.Equals("rg_source_kind", StringComparison.OrdinalIgnoreCase)).Value
                        : default;
                    var name = kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
                    return new RecordingAudioTrack(stream.GetProperty("index").GetInt32(), name switch
                    {
                        "system-audio" => VideoAudioTrackKind.SystemAudio,
                        "microphone" => VideoAudioTrackKind.Microphone,
                        _ => VideoAudioTrackKind.Other,
                    });
                }).ToArray();
                return new(ffmpegPath, path, duration, Array.AsReadOnly(tracks));
            }
            finally
            {
                Kill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await Task.WhenAll(output, errors).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                }
            }
        }

        public async Task PlayAsync(double position, double rate, CancellationToken token = default)
        {
            ValidatePosition(position);
            ValidateRate(rate);
            await _access.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                await StopCoreAsync().ConfigureAwait(false);
                _position = Math.Min(position, _duration);
                _rate = rate;
                _failure = null;
                if (!HasAudio || _position >= _duration) return;
                _session = new(_executable, _path, _position, rate, Mix, token);
                await _session.Ready.ConfigureAwait(false);
            }
            finally
            {
                _access.Release();
            }
        }

        public async Task PauseAsync()
        {
            await _access.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _access.Release();
            }
        }

        public async Task SeekAsync(double position, CancellationToken token = default)
        {
            ValidatePosition(position);
            await _access.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var playing = IsPlaying;
                await StopCoreAsync().ConfigureAwait(false);
                _position = Math.Min(position, _duration);
                if (!playing || !HasAudio || _position >= _duration) return;
                _session = new(_executable, _path, _position, _rate, Mix, token);
                await _session.Ready.ConfigureAwait(false);
            }
            finally
            {
                _access.Release();
            }
        }

        public async Task SetRateAsync(double rate, CancellationToken token = default)
        {
            ValidateRate(rate);
            await _access.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var playing = IsPlaying;
                await StopCoreAsync().ConfigureAwait(false);
                _rate = rate;
                if (!playing || !HasAudio || _position >= _duration) return;
                _session = new(_executable, _path, _position, rate, Mix, token);
                await _session.Ready.ConfigureAwait(false);
            }
            finally
            {
                _access.Release();
            }
        }

        internal async Task SetTrackMixAsync(int streamIndex, bool muted, double gain,
            CancellationToken token = default)
        {
            if (!double.IsFinite(gain) || gain is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(gain));
            if (Tracks.All(track => track.StreamIndex != streamIndex))
                throw new ArgumentOutOfRangeException(nameof(streamIndex));
            await _access.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var playing = IsPlaying;
                await StopCoreAsync().ConfigureAwait(false);
                Volatile.Write(ref _mix, Array.AsReadOnly<RecordingAudioTrackMix>([
                    .. Mix.Select(mix => mix.StreamIndex == streamIndex
                        ? new(streamIndex, muted, gain)
                        : mix),
                ]));
                if (!playing || _position >= _duration) return;
                _session = new(_executable, _path, _position, _rate, Mix, token);
                await _session.Ready.ConfigureAwait(false);
            }
            finally
            {
                _access.Release();
            }
        }

        internal static ProcessStartInfo StartInfo(string executable)
        {
            return new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
        }

        internal static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // The decoder may have exited between the state check and termination.
            }
        }

        internal static async Task<string> ReadErrorsAsync(StreamReader reader, CancellationToken token)
        {
            var tail = "";
            var buffer = new char[2048];
            int count;
            while ((count = await reader.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                tail += new string(buffer, 0, count);
                if (tail.Length > 8192) tail = tail[^8192..];
            }

            return tail;
        }

        private static void ValidatePosition(double position)
        {
            if (!double.IsFinite(position) || position < 0)
                throw new ArgumentOutOfRangeException(nameof(position));
        }

        private static void ValidateRate(double rate)
        {
            if (!double.IsFinite(rate) || rate is < 0.25 or > 4)
                throw new ArgumentOutOfRangeException(nameof(rate));
        }

        private async Task StopCoreAsync()
        {
            if (_session is not { } session) return;
            await session.StopAsync().ConfigureAwait(false);
            _position = Math.Min(_duration, session.Position);
            _failure = session.Failure;
            _session = null;
        }
    }
}
