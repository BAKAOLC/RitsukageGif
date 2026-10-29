namespace RitsukageGif.Core
{
    public sealed class ToneMappedFrameSource(IColorFrameSource source, HdrToneMapMode mode) : IFrameSource
    {
        public byte[] Capture()
        {
            return Capture(CancellationToken.None);
        }

        public byte[] Capture(CancellationToken token)
        {
            var frames = source.CaptureFrames(token);
            var sdr = frames.Hdr is { } hdr ? HdrToneMapping.Map(hdr, mode, token) : frames.Sdr;
            return CapturedFramePixels.ToStraightBgra(sdr, token);
        }

        public void Dispose()
        {
            source.Dispose();
        }
    }
}
