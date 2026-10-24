namespace RitsukageGif.Core
{
    internal sealed record AnimationEditSnapshot(IReadOnlyList<AnimationEditFrame> Frames, uint Iterations);
}
