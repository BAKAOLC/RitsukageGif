using MiniAudioEx.Native;

namespace RitsukageGif.Desktop
{
    internal sealed class SoundPlayback
    {
        private readonly Lock _gate = new();
        private CancellationTokenSource? _cancellation;
        private Task _pending = Task.CompletedTask;

        public Task PlayAsync(string path)
        {
            lock (_gate)
            {
                _cancellation?.Cancel();
                var previous = _pending;
                var cancellation = new CancellationTokenSource();
                _cancellation = cancellation;
                return _pending = Task.Run(async () =>
                {
                    try
                    {
                        // Finish releasing the previous native device before opening another one.
                        try
                        {
                            await previous.ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            /* The original caller observes playback errors. */
                        }

                        cancellation.Token.ThrowIfCancellationRequested();
                        await PlayCoreAsync(path, cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
                            cancellation.Dispose();
                        }
                    }
                }, CancellationToken.None);
            }
        }

        public Task StopAsync()
        {
            lock (_gate)
            {
                _cancellation?.Cancel();
                return _pending;
            }
        }

        private static async Task PlayCoreAsync(string path, CancellationToken cancellationToken)
        {
            using var resource = await MediaResource.OpenAsync(path, cancellationToken).ConfigureAwait(false);
            path = resource.Path;
            var device = new ma_ex_device_info { index = -1 };
            var config = MiniAudioExNative.ma_ex_context_config_init(48000, 2, 0, ref device);
            var context = MiniAudioExNative.ma_ex_context_init(ref config);
            if (context == 0) throw new IOException(UiText.Get("AudioDeviceUnavailable"));
            var sound = default(ma_sound_ptr);
            var loaded = false;
            try
            {
                var engine = MiniAudioExNative.ma_ex_context_get_engine(context);
                cancellationToken.ThrowIfCancellationRequested();
                sound = new(true);
                if (sound.pointer == 0) throw new OutOfMemoryException();
                const ma_sound_flags flags = ma_sound_flags.stream | ma_sound_flags.no_spatialization;
                Check(OperatingSystem.IsWindows()
                    ? SoundNative.LoadWide(engine, path, flags, 0, 0, sound.pointer)
                    : SoundNative.LoadUtf8(engine, path, flags, 0, 0, sound.pointer));
                loaded = true;
                cancellationToken.ThrowIfCancellationRequested();
                Check(MiniAudioNative.ma_sound_start(sound));
                while (MiniAudioNative.ma_sound_is_playing(sound) != 0)
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                // The decoder can finish while the device still has queued samples.
                await Task.Delay(GetDrainDelay(new(engine)), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (loaded)
                {
                    MiniAudioNative.ma_sound_stop(sound);
                    MiniAudioNative.ma_sound_uninit(sound);
                }

                sound.Free();
                MiniAudioExNative.ma_ex_context_uninit(context);
            }
        }

        private static unsafe TimeSpan GetDrainDelay(ma_engine_ptr engine)
        {
            var playback = MiniAudioNative.ma_device_get_playback(MiniAudioNative.ma_engine_get_device(engine)).Get();
            if (playback == null || playback->internalSampleRate == 0) return TimeSpan.FromMilliseconds(100);
            var frames = playback->internalPeriodSizeInFrames * (playback->internalPeriods + 1d);
            return TimeSpan.FromSeconds(Math.Clamp(frames / playback->internalSampleRate, 0.02, 2));
        }

        private static void Check(ma_result result)
        {
            if (result != ma_result.success) throw new IOException($"Audio: {result}");
        }
    }
}
