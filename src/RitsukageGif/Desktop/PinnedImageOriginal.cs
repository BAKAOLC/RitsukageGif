namespace RitsukageGif.Desktop
{
    internal sealed record PinnedImageOriginal(byte[] Png, byte[]? SdrPng, CapturedFrame? Hdr)
    {
        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public byte[]? Encoded { get; init; }
        public AnimationPinMetadata? Animation { get; init; }
    }
}
