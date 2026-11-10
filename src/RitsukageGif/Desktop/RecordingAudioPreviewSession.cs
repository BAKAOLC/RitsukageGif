using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using MiniAudioEx.Native;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingAudioPreviewSession
    {
        private const int FrameBytes = 8;
        private const int SampleRate = 48000;
        private readonly byte[] _buffer = new byte[SampleRate * FrameBytes / 2];
        private readonly ma_device_data_proc _callback;
        private readonly double _origin;
        private readonly double _rate;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _stop;
        private bool _buffering = true;
        private long _read;
        private long _written;
        private readonly IReadOnlyList<RecordingAudioTrackMix> _mix;

        internal RecordingAudioPreviewSession(string executable, string path, double position, double rate,
            IReadOnlyList<RecordingAudioTrackMix> mix, CancellationToken token)
        {
            _origin = position;
            _rate = rate;
            _mix = mix;
            _callback = Receive;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            Completion = Task.Run(() => RunAsync(executable, path), CancellationToken.None);
        }

        internal Task Ready => _ready.Task;
        internal Task Completion { get; }
        internal double Position => _origin + Volatile.Read(ref _read) / (double)(SampleRate * FrameBytes) * _rate;
        internal string? Failure { get; private set; }
        internal bool IsBuffering => Volatile.Read(ref _buffering);

        internal async Task StopAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await Completion.ConfigureAwait(false);
            _stop.Dispose();
        }

        private async Task RunAsync(string executable, string path)
        {
            var device = default(ma_device_ptr);
            var initialized = false;
            Exception? failure = null;
            var token = _stop.Token;
            try
            {
                device = new(true);
                if (device.pointer == 0) throw new OutOfMemoryException();
                var config = MiniAudioNative.ma_device_config_init(ma_device_type.playback);
                config.sampleRate = SampleRate;
                config.playback.format = ma_format.f32;
                config.playback.channels = 2;
                config.playback.shareMode = ma_share_mode.shared;
                config.periodSizeInMilliseconds = 10;
                config.SetDataCallback(_callback);
                Check(MiniAudioNative.ma_device_init(default, ref config, device));
                initialized = true;
                using var process = new Process();
                process.StartInfo = RecordingAudioPreview.StartInfo(executable);
                foreach (var argument in Arguments(path)) process.StartInfo.ArgumentList.Add(argument);
                token.ThrowIfCancellationRequested();
                process.Start();
                // ReSharper disable once AccessToDisposedClosure
                await using var registration = token.Register(() => RecordingAudioPreview.Kill(process));
                using var decodeStop = CancellationTokenSource.CreateLinkedTokenSource(token);
                var errors = RecordingAudioPreview.ReadErrorsAsync(process.StandardError, CancellationToken.None);
                var producer = ProduceAsync(process.StandardOutput.BaseStream, decodeStop.Token);
                try
                {
                    // Prefill one tenth of a second without retaining PCM for the entire recording.
                    while (Volatile.Read(ref _written) < SampleRate * FrameBytes / 10 && !producer.IsCompleted)
                        await Task.Delay(5, token).ConfigureAwait(false);
                    if (producer.IsFaulted) await producer.ConfigureAwait(false);
                    Check(MiniAudioNative.ma_device_start(device));
                    _ready.TrySetResult();
                    await producer.ConfigureAwait(false);
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                    var detail = await errors.ConfigureAwait(false);
                    if (process.ExitCode != 0) throw new IOException($"Audio decoder failed: {detail}");
                    if (Volatile.Read(ref _written) % FrameBytes != 0)
                        throw new InvalidDataException("Audio decoder returned an incomplete PCM frame.");
                    while (Volatile.Read(ref _read) < Volatile.Read(ref _written))
                        await Task.Delay(5, token).ConfigureAwait(false);
                    await Task.Delay(DrainDelay(device), token).ConfigureAwait(false);
                }
                finally
                {
                    await decodeStop.CancelAsync().ConfigureAwait(false);
                    RecordingAudioPreview.Kill(process);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    // Closing the pipe also interrupts a pending read after a native output failure.
                    await process.StandardOutput.BaseStream.DisposeAsync().ConfigureAwait(false);
                    try
                    {
                        await producer.ConfigureAwait(false);
                    }
                    catch (Exception) when (decodeStop.IsCancellationRequested)
                    {
                        // The initiating operation reports the failure after native teardown.
                    }

                    await errors.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                failure = error;
                Failure = error.Message;
                LogHub.GetLogger("Desktop.Recording.Audio").ZLogError(error,
                    $"Recording audio preview session failed. Position={Position} Rate={_rate}");
            }
            finally
            {
                if (initialized)
                {
                    MiniAudioNative.ma_device_stop(device);
                    MiniAudioNative.ma_device_uninit(device);
                }

                device.Free();
                GC.KeepAlive(_callback);
                if (failure is not null) _ready.TrySetException(failure);
                else _ready.TrySetCanceled(token.IsCancellationRequested ? token : new(true));
            }
        }

        private IEnumerable<string> Arguments(string path)
        {
            var tempo = _rate < 0.5
                ? $"atempo=0.5,atempo={(_rate * 2).ToString(CultureInfo.InvariantCulture)}"
                : _rate > 2
                    ? $"atempo=2,atempo={(_rate / 2).ToString(CultureInfo.InvariantCulture)}"
                    : $"atempo={_rate.ToString(CultureInfo.InvariantCulture)}";
            var inputs = _mix.Select((mix, index) =>
                $"[0:{mix.StreamIndex}]aresample=async=1:first_pts=0,volume={(mix.Muted ? 0 : mix.Gain).ToString(CultureInfo.InvariantCulture)}[a{index}]");
            var labels = string.Concat(Enumerable.Range(0, _mix.Count).Select(index => $"[a{index}]"));
            var filter = string.Join(';', inputs) + ";" + labels +
                         $"amix=inputs={_mix.Count}:normalize=0:duration=longest" +
                         (_rate.Equals(1d) ? "" : "," + tempo) +
                         "[mix]";
            return
            [
                "-hide_banner", "-loglevel", "error", "-nostdin", "-ss",
                _origin.ToString(CultureInfo.InvariantCulture), "-i", path, "-filter_complex", filter, "-map", "[mix]",
                "-vn", "-sn", "-dn",
                "-ar",
                SampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "2",
                "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1",
            ];
        }

        private async Task ProduceAsync(Stream stream, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var written = Volatile.Read(ref _written);
                var free = _buffer.Length - (int)(written - Volatile.Read(ref _read));
                if (free == 0)
                {
                    await Task.Delay(5, token).ConfigureAwait(false);
                    continue;
                }

                var offset = (int)(written % _buffer.Length);
                var length = Math.Min(free, _buffer.Length - offset);
                var count = await stream.ReadAsync(_buffer.AsMemory(offset, length), token).ConfigureAwait(false);
                if (count == 0) return;
                Volatile.Write(ref _written, written + count);
            }
        }

        private unsafe void Receive(ma_device_ptr device, nint output, nint input, uint frameCount)
        {
            var destination = new Span<float>((void*)output, checked((int)frameCount * 2));
            destination.Clear();
            var read = Volatile.Read(ref _read);
            var count = (int)Math.Min(frameCount * FrameBytes, Volatile.Read(ref _written) - read);
            count -= count % FrameBytes;
            Volatile.Write(ref _buffering, count < frameCount * FrameBytes);
            var offset = (int)(read % _buffer.Length);
            var bytes = MemoryMarshal.AsBytes(destination);
            var first = Math.Min(count, _buffer.Length - offset);
            _buffer.AsSpan(offset, first).CopyTo(bytes);
            _buffer.AsSpan(0, count - first).CopyTo(bytes[first..]);
            Volatile.Write(ref _read, read + count);
        }

        private static unsafe TimeSpan DrainDelay(ma_device_ptr device)
        {
            var playback = MiniAudioNative.ma_device_get_playback(device).Get();
            if (playback == null || playback->internalSampleRate == 0) return TimeSpan.FromMilliseconds(100);
            return TimeSpan.FromSeconds(Math.Clamp(
                playback->internalPeriodSizeInFrames * (playback->internalPeriods + 1d) / playback->internalSampleRate,
                0.02, 2));
        }

        private static void Check(ma_result result)
        {
            if (result != ma_result.success) throw new IOException($"Audio output unavailable: {result}");
        }
    }
}
