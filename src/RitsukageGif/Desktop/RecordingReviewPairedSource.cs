using System.Buffers;
using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingReviewPairedSource(RecordingReviewSource sdr, RecordingReviewSource hdr)
        : RecordingReviewSource
    {
        internal override int Width => sdr.Width;
        internal override int Height => sdr.Height;
        internal override int OriginalWidth => sdr.OriginalWidth;
        internal override int OriginalHeight => sdr.OriginalHeight;
        internal override double Duration => sdr.Duration;
        internal override bool IsHdr => hdr.IsHdr;
        internal override bool HasAudio => sdr.HasAudio;

        internal static async Task<RecordingReviewSource> OpenAsync(RecordingReviewDocument document,
            PixelSize viewport, CancellationToken token)
        {
            var sdr = await OpenAsync(document.Paths[0], document.Video, viewport, token);
            try
            {
                var hdr = await OpenAsync(document.Paths[1], document.Video, viewport, token);
                return new RecordingReviewPairedSource(sdr, hdr);
            }
            catch
            {
                await sdr.DisposeAsync();
                throw;
            }
        }

        internal override async Task SetViewportAsync(PixelSize viewport, CancellationToken token)
        {
            await sdr.SetViewportAsync(viewport, token);
            await hdr.SetViewportAsync(viewport, token);
        }

        internal override async Task<PreviewFrame> FrameAtAsync(double position, CancellationToken token)
        {
            using var mapped = await sdr.FrameAtAsync(position, token);
            using var linear = await hdr.FrameAtAsync(position, token);
            token.ThrowIfCancellationRequested();
            if (mapped.Width != linear.Width || mapped.Height != linear.Height)
                throw new InvalidDataException("Recorded SDR and HDR frame geometry does not match.");
            var count = checked(mapped.Width * mapped.Height);
            var pixels = ArrayPool<byte>.Shared.Rent(checked(count * 4));
            byte[]? hdrPixels = null;
            try
            {
                mapped.Pixels.AsSpan(0, count * 4).CopyTo(pixels);
                if (linear.HdrPixels is { } source)
                {
                    hdrPixels = ArrayPool<byte>.Shared.Rent(checked(count * 8));
                    source.AsSpan(0, count * 8).CopyTo(hdrPixels);
                }

                return new(pixels, mapped.Width, mapped.Height, mapped.Delay, hdrPixels,
                    linear.HdrReferenceWhiteLevel, linear.HdrAlphaMode);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(pixels);
                if (hdrPixels is not null) ArrayPool<byte>.Shared.Return(hdrPixels);
                throw;
            }
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await sdr.DisposeAsync();
            }
            finally
            {
                await hdr.DisposeAsync();
            }
        }
    }
}
