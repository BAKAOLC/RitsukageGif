namespace RitsukageGif.Contracts
{
    public sealed record VideoCaptureCapabilities(
        bool Available,
        bool Mp4,
        bool WebM,
        bool Mp4Audio,
        bool WebMAudio,
        bool NativeAudio,
        IReadOnlyList<string> AudioInputFormats,
        bool Mp4Hdr = false,
        bool WebMHdr = false);
}
