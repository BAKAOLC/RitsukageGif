using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed class MediaProcessingService : IMediaProcessingService
    {
        private const long PixelBudget = 512L * 1024 * 1024;

        public Task<MediaInfo> InspectAsync(string path, CancellationToken token = default)
        {
            return Task.Run(() => Inspect(Path.GetFullPath(path), token), token);
        }

        public Task<MediaInfo> ProcessAsync(string input, string output, MediaProcessOptions options,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            return Task.Run(() => Process(Path.GetFullPath(input), Path.GetFullPath(output), options, token), token);
        }

        private static MediaInfo Inspect(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (MediaPngCodec.Inspect(path, token) is { } png) return png;
            using var images = new MagickImageCollection();
            images.Ping(path);
            token.ThrowIfCancellationRequested();
            if (images.Count == 0) throw new InvalidDataException();
            var width = images.Max(image => Math.Max(image.Width, image.Page.Width));
            var height = images.Max(image => Math.Max(image.Height, image.Page.Height));
            return new MediaInfo(path, new FileInfo(path).Length, checked((int)width), checked((int)height),
                images.Count, images.Sum(image =>
                    image.AnimationDelay /
                    (double)(image.AnimationTicksPerSecond == 0 ? 100 : image.AnimationTicksPerSecond)),
                images[0].Format.ToString().ToLowerInvariant());
        }

        private static MediaInfo Process(string input, string output, MediaProcessOptions options,
            CancellationToken token)
        {
            if (options.Effects is { } effects)
            {
                if (effects.Count > 64) throw new ArgumentException(nameof(options.Effects));
                foreach (var effect in effects)
                {
                    ArgumentNullException.ThrowIfNull(effect);
                    effect.Validate();
                }
            }

            var sourceEffects = options.Effects?.Where(effect => effect.Space == PixelEffectSpace.Source).ToArray();
            var outputEffects = options.Effects?.Where(effect => effect.Space == PixelEffectSpace.Output).ToArray();
            var info = Inspect(input, token);
            if (info.Bytes > PixelBudget) throw new InvalidOperationException(UiText.Get("MemoryLimit"));
            var format = (options.Format ?? Path.GetExtension(output).TrimStart('.')).ToLowerInvariant();
            if (format is not ("gif" or "webp" or "png" or "apng"))
                throw new ArgumentOutOfRangeException(nameof(options.Format));
            if (options.FrameIndex is < 0 || options.FrameIndex >= info.Frames)
                throw new ArgumentOutOfRangeException(nameof(options.FrameIndex));
            if (format == "png" && info.Frames > 1 && !options.FrameIndex.HasValue)
                throw new ArgumentException(nameof(options.Format));
            if (options.Colors is < 2 or > 256) throw new ArgumentOutOfRangeException(nameof(options.Colors));
            if (options.Quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(options.Quality));
            if (options.Width is <= 0 || options.Height is <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.Width));
            if (options.Rotation is not (0 or 90 or 180 or 270))
                throw new ArgumentOutOfRangeException(nameof(options.Rotation));
            var crop = options.CropX.HasValue || options.CropY.HasValue || options.CropWidth.HasValue ||
                       options.CropHeight.HasValue;
            if (crop && (options.CropX is null or < 0 || options.CropY is null or < 0 ||
                         options.CropWidth is null or <= 0 || options.CropHeight is null or <= 0 ||
                         (long)options.CropX + options.CropWidth > info.Width ||
                         (long)options.CropY + options.CropHeight > info.Height))
                throw new ArgumentOutOfRangeException(nameof(options.CropWidth));
            var width = options.CropWidth ?? info.Width;
            var height = options.CropHeight ?? info.Height;
            if (options.Rotation is 90 or 270) (width, height) = (height, width);
            var targetWidth = options.Width ?? (options.Height.HasValue
                ? checked((int)Math.Max(1, Math.Round(width * (double)options.Height.Value / height)))
                : width);
            var targetHeight = options.Height ?? (options.Width.HasValue
                ? checked((int)Math.Max(1, Math.Round(height * (double)options.Width.Value / width)))
                : height);
            CheckBudget(info.Width, info.Height, info.Frames);
            CheckBudget(targetWidth, targetHeight, info.Frames);
            if (!options.Overwrite && File.Exists(output)) throw new IOException(output);
            var directory = Path.GetDirectoryName(output)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using var images = info.Format is "png" or "apng"
                    ? MediaPngCodec.ReadImages(input)
                    : new MagickImageCollection(input);
                if (images.Count != info.Frames) throw new InvalidDataException();
                var decodedWidth = checked((int)images.Max(image => Math.Max(image.Width, image.Page.Width)));
                var decodedHeight = checked((int)images.Max(image => Math.Max(image.Height, image.Page.Height)));
                CheckBudget(decodedWidth, decodedHeight, images.Count);
                if (decodedWidth != info.Width || decodedHeight != info.Height) throw new InvalidDataException();
                if (info.Format == "apng") images[0].AnimationIterations = MediaPngCodec.ReadIterations(input);
                token.ThrowIfCancellationRequested();
                foreach (var image in images)
                    image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                if (info.Format == "apng") MediaPngCodec.Coalesce(images, token);
                else images.Coalesce();
                if (options.FrameIndex is { } frameIndex)
                {
                    using var selected = images[frameIndex].Clone();
                    foreach (var image in images) image.Dispose();
                    images.Clear();
                    images.Add(selected.Clone());
                    images[0].AnimationDelay = 0;
                }

                foreach (var image in images)
                {
                    image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                    token.ThrowIfCancellationRequested();
                    if (crop)
                        image.Crop(new MagickGeometry(options.CropX!.Value, options.CropY!.Value,
                            (uint)options.CropWidth!.Value, (uint)options.CropHeight!.Value));
                    image.ResetPage();
                    ApplyEffects(image, sourceEffects, token);
                    if (options.FlipHorizontal) image.Flop();
                    if (options.FlipVertical) image.Flip();
                    if (options.Rotation != 0) image.Rotate(options.Rotation);
                    if (image.Width != targetWidth || image.Height != targetHeight)
                        image.Resize(new MagickGeometry((uint)targetWidth, (uint)targetHeight)
                            { IgnoreAspectRatio = true });
                    image.ResetPage();
                    ApplyEffects(image, outputEffects, token);
                    image.Quality = (uint)options.Quality;
                    if (format == "gif")
                        image.Quantize(new QuantizeSettings
                        {
                            Colors = (uint)options.Colors,
                            DitherMethod = options.Dither ? DitherMethod.FloydSteinberg : DitherMethod.No
                        });
                    if (format == "webp") image.Settings.SetDefine(MagickFormat.WebP, "lossless", options.Lossless);
                }

                token.ThrowIfCancellationRequested();
                if (format == "apng") MediaPngCodec.Write(images, temporary, token);
                else
                    images.Write(temporary, format switch
                    {
                        "gif" => MagickFormat.Gif, "webp" => MagickFormat.WebP, _ => MagickFormat.Png
                    });
                token.ThrowIfCancellationRequested();
                var result = Inspect(temporary, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, output, options.Overwrite);
                return result with { Path = output };
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void ApplyEffects(IMagickImage<byte> image, IReadOnlyList<PixelEffect>? effects,
            CancellationToken token)
        {
            if (effects is not { Count: > 0 }) return;
            byte[] pixels;
            using (var collection = image.GetPixels()) pixels = collection.ToByteArray(PixelMapping.BGRA)!;
            PixelEffectProcessor.Apply(pixels, checked((int)image.Width), checked((int)image.Height), effects, token);
            var delay = image.AnimationDelay;
            var ticks = image.AnimationTicksPerSecond;
            var iterations = image.AnimationIterations;
            var dispose = image.GifDisposeMethod;
            image.ReadPixels(pixels,
                new PixelReadSettings(image.Width, image.Height, StorageType.Char, PixelMapping.BGRA));
            image.AnimationDelay = delay;
            image.AnimationTicksPerSecond = ticks;
            image.AnimationIterations = iterations;
            image.GifDisposeMethod = dispose;
        }

        private static void CheckBudget(int width, int height, int frames)
        {
            // Bound the two full RGBA collections needed while coalescing. Native codec scratch space is additional.
            if (width <= 0 || height <= 0 || frames <= 0 ||
                width * (double)height * frames * 8 > PixelBudget)
                throw new InvalidOperationException(UiText.Get("MemoryLimit"));
        }
    }
}
