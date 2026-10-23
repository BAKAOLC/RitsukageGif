namespace RitsukageGif.Desktop
{
    internal interface ISceneWindowSource : IDisposable
    {
        int Width { get; }
        int Height { get; }
        byte[]? Capture(CancellationToken token);
    }
}
