namespace RitsukageGif.Desktop
{
    internal interface ISceneWindowSource : IDisposable
    {
        int Width { get; }

        int Height { get; }

        // A nonnegative revision changes whenever pixel contents or dimensions change.
        long FrameRevision => -1;

        // True only when a returned array will never be modified by later captures.
        bool OwnsFrameBuffers => false;
        byte[]? Capture(CancellationToken token);
    }
}
