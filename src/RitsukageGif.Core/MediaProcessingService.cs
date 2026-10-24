using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed partial class MediaProcessingService : IMediaProcessingService, IMediaOptimizationService
    {
        public Task<MediaOptimizationResult> OptimizeAsync(string input, string output,
            MediaOptimizationOptions options, CancellationToken token = default) =>
            new MediaOptimizationService(this).OptimizeAsync(input, output, options, token);

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

        internal static Task<MediaInfo> ProcessPreparedAsync(PreparedMediaInput input, string output,
            MediaProcessOptions options, CancellationToken token)
        {
            return Task.Run(() => Process(input.Info.Path, output, options, token, input), token);
        }

        internal static MagickImageCollection Decode(string input, MediaInfo info, CancellationToken token)
        {
            CheckBudget(info.Width, info.Height, info.Frames);
            if (info.Bytes > PixelBudget) throw new InvalidOperationException(UiText.Get("MemoryLimit"));
            token.ThrowIfCancellationRequested();
            var images = info.Format is "png" or "apng"
                ? MediaPngCodec.ReadImages(input, token)
                : new MagickImageCollection();
            try
            {
                if (info.Format is not ("png" or "apng"))
                    images.ReadAsync(input, token).GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
                if (images.Count != info.Frames) throw new InvalidDataException();
                var width = checked((int)images.Max(image => Math.Max(image.Width, image.Page.Width)));
                var height = checked((int)images.Max(image => Math.Max(image.Height, image.Page.Height)));
                CheckBudget(width, height, images.Count);
                if (width != info.Width || height != info.Height) throw new InvalidDataException();
                if (info.Format == "apng") images[0].AnimationIterations = MediaPngCodec.ReadIterations(input);
                foreach (var image in images)
                    image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                if (info.Format == "apng") MediaPngCodec.Coalesce(images, token);
                else if (info.Format == "webp") MediaWebpCodec.Coalesce(input, images, info.Width, info.Height, token);
                else images.Coalesce();
                token.ThrowIfCancellationRequested();
                return images;
            }
            catch
            {
                images.Dispose();
                throw;
            }
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
            CancellationToken token, PreparedMediaInput? prepared = null)
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
            var info = prepared?.Info ?? Inspect(input, token);
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
            var singleApngFrame = info.Format == "apng" && options.FrameIndex.HasValue &&
                                  (prepared is null || prepared.FrameIndex == options.FrameIndex);
            var streamApng = prepared is null && info.Format == "apng" && format == "apng" &&
                             !options.FrameIndex.HasValue;
            if (!singleApngFrame && !streamApng && info.Bytes > PixelBudget)
                throw new InvalidOperationException(UiText.Get("MemoryLimit"));
            // Streaming APNG extraction holds the canvas, current frame and optional previous canvas.
            CheckBudget(info.Width, info.Height, singleApngFrame || streamApng ? 2 : info.Frames);
            CheckBudget(targetWidth, targetHeight, options.FrameIndex.HasValue || streamApng ? 1 : info.Frames);
            if (!options.Overwrite && File.Exists(output)) throw new IOException(output);
            var directory = Path.GetDirectoryName(output)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            try
            {
                if (streamApng)
                    MediaPngCodec.Write(TransformFrames(MediaPngCodec.ReadCoalescedFrames(input, info, token),
                            options, crop, targetWidth, targetHeight, sourceEffects, outputEffects, token),
                        info.Frames, temporary, token);
                else
                {
                    using var images = prepared is null
                        ? singleApngFrame
                            ? MediaPngCodec.ReadFrame(input, options.FrameIndex!.Value, info, token)
                            : Decode(input, info, token)
                        : Clone(prepared.GetImages(token), prepared.FrameIndex.HasValue ? 0 : options.FrameIndex,
                            token);
                    if (options.FrameIndex is { } frameIndex)
                    {
                        if (prepared is null && !singleApngFrame)
                        {
                            // Remove unwanted frames in place; extracting one frame requires no pixel clone.
                            for (var index = images.Count - 1; index >= 0; index--)
                                if (index != frameIndex)
                                {
                                    images[index].Dispose();
                                    images.RemoveAt(index);
                                }
                        }

                        images[0].AnimationDelay = 0;
                    }

                    foreach (var image in images)
                        Transform(image, options, crop, targetWidth, targetHeight, format, sourceEffects, outputEffects,
                            token);

                    token.ThrowIfCancellationRequested();
                    if (format == "apng") MediaPngCodec.Write(images, temporary, token);
                    else
                        images.Write(temporary, format switch
                        {
                            "gif" => MagickFormat.Gif, "webp" => MagickFormat.WebP, _ => MagickFormat.Png
                        });
                }

                token.ThrowIfCancellationRequested();
                var result = Inspect(temporary, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, output, options.Overwrite);
                return result with { Path = output };
            }
            // A cancelled native transform can invalidate its image instead of throwing MagickException.
            catch (Exception exception) when (token.IsCancellationRequested &&
                                              exception is MagickException or ObjectDisposedException)
            {
                throw new OperationCanceledException(token);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void Transform(IMagickImage<byte> image, MediaProcessOptions options, bool crop,
            int targetWidth, int targetHeight, string format, IReadOnlyList<PixelEffect>? sourceEffects,
            IReadOnlyList<PixelEffect>? outputEffects, CancellationToken token)
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

        private static IEnumerable<IMagickImage<byte>> TransformFrames(IEnumerable<IMagickImage<byte>> frames,
            MediaProcessOptions options, bool crop, int width, int height,
            IReadOnlyList<PixelEffect>? sourceEffects, IReadOnlyList<PixelEffect>? outputEffects,
            CancellationToken token)
        {
            foreach (var image in frames)
            {
                Transform(image, options, crop, width, height, "apng", sourceEffects, outputEffects, token);
                yield return image;
            }
        }

        private static MagickImageCollection Clone(MagickImageCollection source, int? frameIndex,
            CancellationToken token)
        {
            var result = new MagickImageCollection();
            try
            {
                var start = frameIndex ?? 0;
                var end = frameIndex.HasValue ? start + 1 : source.Count;
                for (var index = start; index < end; index++)
                {
                    token.ThrowIfCancellationRequested();
                    result.Add(source[index].Clone());
                }

                return result;
            }
            catch
            {
                result.Dispose();
                throw;
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
