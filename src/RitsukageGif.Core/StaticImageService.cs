using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed class StaticImageService : IStaticImageService
    {
        private const long PixelBudget = 256L * 1024 * 1024;
        private static readonly SemaphoreSlim Gate = new(2);
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Image");

        public Task<MediaInfo> SaveAsync(ReadOnlyMemory<byte> encodedImage, string output,
            StaticImageSaveOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (encodedImage.IsEmpty) throw new ArgumentException(null, nameof(encodedImage));
            return Run(() =>
            {
                using var image = new MagickImage();
                image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                image.Ping(encodedImage.Span);
                ValidateDimensions(image.Width, image.Height);
                token.ThrowIfCancellationRequested();
                image.Read(encodedImage.Span);
                return Save(image, output, options, token);
            }, token);
        }

        public Task<MediaInfo> SavePixelsAsync(ReadOnlyMemory<byte> bgra, int width, int height, string output,
            StaticImageSaveOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            ValidateDimensions((uint)width, (uint)height);
            if (bgra.Length != (long)width * height * 4) throw new ArgumentException(null, nameof(bgra));
            return Run(() =>
            {
                using var image = new MagickImage();
                image.ReadPixels(bgra.Span,
                    new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA));
                return Save(image, output, options, token);
            }, token);
        }

        private static async Task<MediaInfo> Run(Func<MediaInfo> operation, CancellationToken token)
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            Logger.ZLogDebug($"Static image output started");
            try
            {
                var result = await Task.Run(operation, token).ConfigureAwait(false);
                Logger.ZLogInformation(
                    $"Static image output completed: output={result.Path}, format={result.Format}, width={result.Width}, height={result.Height}, bytes={result.Bytes}");
                return result;
            }
            catch (Exception error) when (token.IsCancellationRequested &&
                                          error is MagickException or ObjectDisposedException)
            {
                Logger.ZLogDebug($"Static image output canceled during native processing");
                throw new OperationCanceledException(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Static image output canceled");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"Static image output failed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static MediaInfo Save(MagickImage image, string output, StaticImageSaveOptions options,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ValidateDimensions(image.Width, image.Height);
            var format = options.Format ?? Path.GetExtension(output).ToLowerInvariant() switch
            {
                ".png" => StaticImageFormat.Png,
                ".jpg" or ".jpeg" => StaticImageFormat.Jpeg,
                ".bmp" => StaticImageFormat.Bmp,
                ".webp" => StaticImageFormat.WebP,
                _ => throw new ArgumentOutOfRangeException(nameof(output)),
            };
            if (!Enum.IsDefined(format) || options.Quality is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(options));
            output = Path.GetFullPath(output);
            if (!options.Overwrite && File.Exists(output)) throw new IOException(output);
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            image.ResetPage();
            image.Quality = format switch
            {
                StaticImageFormat.Jpeg => (uint)Math.Max(1, options.Quality),
                StaticImageFormat.WebP => (uint)options.Quality,
                _ => 90,
            };
            if (format is StaticImageFormat.Jpeg or StaticImageFormat.Bmp)
            {
                var background = new MagickColor(options.Background);
                if (background.A != byte.MaxValue) throw new ArgumentException(nameof(options.Background));
                image.BackgroundColor = background;
                image.Alpha(AlphaAction.Remove);
                image.ColorType = ColorType.TrueColor;
                image.Depth = 8;
                // Preserve colored text and UI edges rather than subsampling the chroma channels.
                if (format == StaticImageFormat.Jpeg)
                    image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:4:4");
            }

            var directory = Path.GetDirectoryName(output)!;
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            try
            {
                image.Write(temporary, format switch
                {
                    StaticImageFormat.Png => MagickFormat.Png32,
                    StaticImageFormat.Jpeg => MagickFormat.Jpeg,
                    StaticImageFormat.WebP => MagickFormat.WebP,
                    _ => MagickFormat.Bmp3,
                });
                token.ThrowIfCancellationRequested();
                var info = new MediaInfo(output, new FileInfo(temporary).Length, checked((int)image.Width),
                    checked((int)image.Height), 1, 0, format.ToString().ToLowerInvariant());
                File.Move(temporary, output, options.Overwrite);
                return info;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void ValidateDimensions(uint width, uint height)
        {
            if (width == 0 || height == 0 || width * (double)height * 4 > PixelBudget)
                throw new InvalidOperationException(UiText.Get("MemoryLimit"));
        }
    }
}
