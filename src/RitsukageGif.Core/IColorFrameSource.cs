namespace RitsukageGif.Core
{
    public interface IColorFrameSource : IDisposable
    {
        CapturedFrameSet CaptureFrames(CancellationToken token = default);
    }
}
