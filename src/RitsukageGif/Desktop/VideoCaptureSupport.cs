namespace RitsukageGif.Desktop
{
    internal static class VideoCaptureSupport
    {
        internal static string? ColorError(OutputColorMode mode, VideoContainer container,
            VideoRecordingCapabilities? capabilities)
        {
            if (!Enum.IsDefined(mode) || !Enum.IsDefined(container)) return "VideoHdrOutputUnavailable";
            if (mode == OutputColorMode.Sdr) return null;
            var supported = container == VideoContainer.Mp4 ? capabilities?.Mp4Hdr : capabilities?.WebMHdr;
            return supported == true ? null : "VideoHdrOutputUnavailable";
        }
    }
}
