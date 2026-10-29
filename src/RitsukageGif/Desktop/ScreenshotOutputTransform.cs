namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotOutputTransform
    {
        internal static Task<ScreenshotResult> ResizeAsync(ScreenshotResult source, ScreenshotOutputSize size,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var original = PngImageCodec.ReadDimensions(source.Png);
            PngImageCodec.ValidateEncoded(source.Png, token);
            var dimensions = size.Resolve(original.Width, original.Height);
            if (source.Hdr is not null && (source.Hdr.Width != original.Width || source.Hdr.Height != original.Height))
                throw new InvalidDataException("The SDR and HDR screenshot dimensions do not match.");
            if (original.Width == dimensions.Width && original.Height == dimensions.Height)
                return Task.FromResult(source);
            if (source.Hdr is not null && (long)dimensions.Width * dimensions.Height * 8 > 512L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(size), UiText.Get("RecordingTooLarge"));
            return Task.Run(() => source with
            {
                Png = PngImageResampler.Resize(source.Png, dimensions.Width, dimensions.Height, token),
                Hdr = source.Hdr is null ? null : ResizeHdr(source.Hdr, dimensions.Width, dimensions.Height, token),
            }, token);
        }
    }
}
