using ImageMagick;

namespace RitsukageGif.Core
{
    public static class PairedImageService
    {
        // ReSharper disable once UnusedMember.Global
        public static Task<(MediaInfo Sdr, MediaInfo Hdr)> SaveAsync(ReadOnlyMemory<byte> sdrPng,
            CapturedFrame hdr, string sdrPath, string hdrPath, CancellationToken token = default)
        {
            return SaveAsync(sdrPng, hdr, sdrPath, hdrPath, false, token);
        }

        public static async Task<(MediaInfo Sdr, MediaInfo Hdr)> SaveAsync(ReadOnlyMemory<byte> sdrPng,
            CapturedFrame hdr, string sdrPath, string hdrPath, bool overwrite, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(hdr);
            token.ThrowIfCancellationRequested();
            sdrPath = Path.GetFullPath(sdrPath);
            hdrPath = Path.GetFullPath(hdrPath);
            if (!string.Equals(Path.GetExtension(sdrPath), ".png", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(hdrPath), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(UiText.Get("HdrPairedPngRequired"));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(sdrPath, hdrPath, comparison))
                throw new ArgumentException(UiText.Get("StaticCaptureOutputExists"), nameof(hdrPath));
            if (Directory.Exists(sdrPath) || Directory.Exists(hdrPath) ||
                (!overwrite && (File.Exists(sdrPath) || File.Exists(hdrPath))))
                throw new IOException(UiText.Get("HdrOutputExists"));
            var id = Guid.NewGuid().ToString("N");
            var sdrStage = sdrPath + "." + id + ".tmp";
            var hdrStage = hdrPath + "." + id + ".tmp";
            try
            {
                var sdrInfo = await SaveOriginalPngAsync(sdrPng, sdrStage, token).ConfigureAwait(false);
                var hdrInfo = await HdrImageService.SaveAsync(hdr, hdrStage,
                    new() { Format = HdrImageFormat.Png }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                VideoOutputTransaction.Publish([sdrStage, hdrStage], [sdrPath, hdrPath], overwrite);
                return (sdrInfo with { Path = sdrPath }, hdrInfo with { Path = hdrPath });
            }
            finally
            {
                if (File.Exists(sdrStage)) File.Delete(sdrStage);
                if (File.Exists(hdrStage)) File.Delete(hdrStage);
            }
        }

        private static async Task<MediaInfo> SaveOriginalPngAsync(ReadOnlyMemory<byte> png, string path,
            CancellationToken token)
        {
            var dimensions = await Task.Run(() =>
            {
                using var image = new MagickImage();
                image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                try
                {
                    image.Ping(png.Span);
                    if (image.Format != MagickFormat.Png || image.Width == 0 || image.Height == 0 ||
                        (long)image.Width * image.Height > 128L * 1024 * 1024)
                        throw new ArgumentException("Invalid PNG image.", nameof(png));
                    token.ThrowIfCancellationRequested();
                    image.Read(png.Span);
                    token.ThrowIfCancellationRequested();
                    return (Width: checked((int)image.Width), Height: checked((int)image.Height));
                }
                catch (MagickException) when (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }
            }, token).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 81920, FileOptions.Asynchronous))
                await stream.WriteAsync(png, token).ConfigureAwait(false);
            return new(path, png.Length, dimensions.Width, dimensions.Height, 1, 0, "png");
        }
    }
}
