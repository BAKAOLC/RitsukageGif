namespace RitsukageGif.Contracts
{
    // SDR and HDR share the same acquired image. TimestampTicks uses Stopwatch.GetTimestamp().
    public sealed record CapturedFrameSet(CapturedFrame Sdr, CapturedFrame? Hdr, long TimestampTicks);
}
