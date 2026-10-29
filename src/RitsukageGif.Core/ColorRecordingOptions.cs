namespace RitsukageGif.Core
{
    public sealed record ColorRecordingOptions(
        RecordingOptions Sdr,
        string? SdrPath,
        string HdrPath,
        long MaximumBufferedBytes = 512L * 1024 * 1024,
        HdrToneMapMode ToneMap = HdrToneMapMode.HighlightCompression);
}
