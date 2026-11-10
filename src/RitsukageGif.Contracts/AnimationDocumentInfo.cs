namespace RitsukageGif.Contracts
{
    public sealed record AnimationDocumentInfo(
        int Width,
        int Height,
        uint Iterations,
        bool CanUndo,
        bool CanRedo,
        IReadOnlyList<AnimationFrameInfo> Frames);
}
