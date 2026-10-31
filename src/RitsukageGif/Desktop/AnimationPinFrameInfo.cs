namespace RitsukageGif.Desktop
{
    internal sealed record AnimationPinFrameInfo(
        long DurationTicks,
        PixelRect Region,
        byte Disposal = 0,
        byte Blend = 0)
    {
        public IReadOnlyList<ReadOnlyMemory<byte>> Data { get; init; } = [];
    }
}
