namespace RitsukageGif.Core
{
    public interface IAudioCaptureSource : IDisposable
    {
        AudioCaptureFormat Format { get; }

        // The PCM buffer remains valid until consume completes; consumers must copy it before retaining it.
        Task RunAsync(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consume, CancellationToken token);
    }
}
