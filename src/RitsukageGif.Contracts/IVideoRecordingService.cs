namespace RitsukageGif.Contracts
{
    public interface IVideoRecordingService
    {
        Task<VideoCaptureCapabilities> GetCapabilitiesAsync(CancellationToken token);
        Task<VideoCaptureHandle> StartAsync(string output, VideoCaptureOptions options, CancellationToken token);
        Task<VideoCaptureHandle> StopAsync(string id, bool cancel, CancellationToken token);
        Task<VideoCaptureHandle> WaitAsync(string id, CancellationToken token);
    }
}
