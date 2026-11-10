namespace RitsukageGif.Desktop
{
    internal sealed class RecordingAnnotationFrameSource(
        IFrameSource source,
        RecordingAnnotationLayer? layer,
        PixelRect captureRegion,
        RecordingCameraSession? camera = null)
        : IFrameSource, IColorFrameSource, IRecordingCaptureMetadataSource
    {
        private readonly RecordingAnnotationWorker _worker = new();

        public CapturedFrameSet CaptureFrames(CancellationToken token = default)
        {
            var snapshot = layer?.CaptureSnapshot();
            var frames = ((IColorFrameSource)source).CaptureFrames(token);
            layer?.OfferSourceFrame(frames.Sdr);
            if (camera is not null) frames = camera.Composite(frames, captureRegion, token);
            if (snapshot is null || snapshot.Layers.Count == 0) return frames;
            var sdr = _worker.ComposeAsync(snapshot, frames.Sdr, token).GetAwaiter().GetResult();
            var hdr = frames.Hdr is { } originalHdr
                ? _worker.ComposeAsync(snapshot, originalHdr, token).GetAwaiter().GetResult()
                : null;
            return new(sdr, hdr, frames.TimestampTicks);
        }

        public byte[] Capture()
        {
            return Capture(CancellationToken.None);
        }

        public byte[] Capture(CancellationToken token)
        {
            return CapturedFramePixels.ToStraightBgra(CaptureFrames(token).Sdr, token);
        }

        public void Dispose()
        {
            try
            {
                source.Dispose();
            }
            finally
            {
                _worker.Dispose();
            }
        }

        public object? CaptureMetadata => (source as IRecordingCaptureMetadataSource)?.CaptureMetadata;
    }
}
