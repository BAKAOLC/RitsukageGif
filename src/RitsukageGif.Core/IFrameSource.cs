namespace RitsukageGif.Core
{
    public interface IFrameSource : IDisposable
    {
        byte[] Capture();

        byte[] Capture(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Capture();
        }
    }
}
