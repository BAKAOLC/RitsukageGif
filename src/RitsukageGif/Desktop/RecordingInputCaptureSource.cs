using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingInputCaptureSource(
        IFrameSource source,
        RecordingOptions options,
        RecordingInputTraceCollector collector) : IFrameSource, IColorFrameSource, IRecordingCaptureMetadataSource
    {
        public CapturedFrameSet CaptureFrames(CancellationToken token = default)
        {
            if (source is not IColorFrameSource color)
                throw new NotSupportedException("The recording capture source does not provide color frame metadata.");
            var frames = color.CaptureFrames(token);
            CaptureMetadata = collector.CaptureSnapshot(new(TimeSpan.Zero, frames.TimestampTicks,
                options.Region, frames.Sdr.Width, frames.Sdr.Height), token);
            return frames;
        }

        public byte[] Capture()
        {
            return Capture(CancellationToken.None);
        }

        public byte[] Capture(CancellationToken token)
        {
            var pixels = source.Capture(token);
            CaptureMetadata = collector.CaptureSnapshot(new(TimeSpan.Zero, Stopwatch.GetTimestamp(),
                options.Region, options.Width, options.Height), token);
            return pixels;
        }

        public void Dispose()
        {
            source.Dispose();
        }

        public object? CaptureMetadata { get; private set; }
    }
}
