namespace RitsukageGif.Core
{
    // Pixel storage belongs to the hub and remains unchanged for the lifetime of this snapshot.
    public sealed record ExternalInputFrame(
        int Width,
        int Height,
        ReadOnlyMemory<byte> Pixels,
        double Timestamp,
        long Version);
}
