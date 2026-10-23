namespace RitsukageGif.Contracts
{
    public sealed record MediaInfo(
        string Path,
        long Bytes,
        int Width,
        int Height,
        int Frames,
        double DurationSeconds,
        string Format);
}
