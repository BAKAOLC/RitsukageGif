using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed class ImageCompositionService : IImageCompositionService
    {
        private const long PixelBudget = 256L * 1024 * 1024;
        private static readonly SemaphoreSlim Gate = new(2);
        private readonly MediaProcessingService _media = new();

        public Task<MediaInfo> ComposeAsync(string output, ImageCompositionOptions options,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            return Run(async () =>
            {
                Budget(options.Width, options.Height);
                if (options.Layers.Count > 128) throw new ArgumentOutOfRangeException(nameof(options.Layers));
                using var canvas = new MagickImage(new MagickColor(options.Background), (uint)options.Width,
                    (uint)options.Height);
                foreach (var layer in options.Layers)
                {
                    token.ThrowIfCancellationRequested();
                    if (!double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1 ||
                        layer.Rotation is not (0 or 90 or 180 or 270))
                        throw new ArgumentOutOfRangeException(nameof(options.Layers));
                    using var images = await Read(layer.Path, token);
                    if (layer.FrameIndex < 0 || layer.FrameIndex >= images.Count)
                        throw new ArgumentOutOfRangeException(nameof(layer.FrameIndex));
                    using var image = images[layer.FrameIndex].Clone();
                    var crop = layer.CropX.HasValue || layer.CropY.HasValue || layer.CropWidth.HasValue ||
                               layer.CropHeight.HasValue;
                    if (crop)
                    {
                        if (layer.CropX is null or < 0 || layer.CropY is null or < 0 ||
                            layer.CropWidth is null or <= 0 || layer.CropHeight is null or <= 0 ||
                            (long)layer.CropX + layer.CropWidth > image.Width ||
                            (long)layer.CropY + layer.CropHeight > image.Height)
                            throw new ArgumentOutOfRangeException(nameof(layer.CropWidth));
                        image.Crop(new MagickGeometry(layer.CropX.Value, layer.CropY.Value,
                            (uint)layer.CropWidth.Value, (uint)layer.CropHeight.Value));
                        image.ResetPage();
                    }

                    ApplyEffects(image, layer.Effects, PixelEffectSpace.Source, token);
                    if (layer.Rotation != 0) image.Rotate(layer.Rotation);
                    var width = layer.Width ?? (layer.Height.HasValue
                        ? checked((int)Math.Max(1, Math.Round(image.Width * (double)layer.Height.Value / image.Height)))
                        : checked((int)image.Width));
                    var height = layer.Height ?? (layer.Width.HasValue
                        ? checked((int)Math.Max(1, Math.Round(image.Height * (double)layer.Width.Value / image.Width)))
                        : checked((int)image.Height));
                    Budget(width, height);
                    image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
                    image.ResetPage();
                    ApplyEffects(image, layer.Effects, PixelEffectSpace.Output, token);
                    image.Alpha(AlphaAction.Set);
                    image.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, layer.Opacity);
                    canvas.Composite(image, layer.X, layer.Y, CompositeOperator.Over);
                }

                return await Write(canvas, output, options.Format, options.Quality, options.Lossless,
                    options.Overwrite, token);
            }, token);
        }

        public Task<MediaInfo> ContactSheetAsync(string input, string output, ImageContactSheetOptions options,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            return Run(async () =>
            {
                if (options.Columns is < 1 or > 128 || options.MaxFrames is < 1 or > 1024 ||
                    options.FirstFrame < 0 || options.FrameStep < 1)
                    throw new ArgumentOutOfRangeException(nameof(options));
                Budget(options.ThumbnailWidth, options.ThumbnailHeight);
                using var images = await Read(input, token);
                if (options.FirstFrame >= images.Count)
                    throw new ArgumentOutOfRangeException(nameof(options.FirstFrame));
                var count = (int)Math.Min(options.MaxFrames,
                    ((long)images.Count - 1 - options.FirstFrame) / options.FrameStep + 1);
                var columns = Math.Min(count, options.Columns);
                var width = checked(columns * options.ThumbnailWidth);
                var height = checked(((count + columns - 1) / columns) * options.ThumbnailHeight);
                Budget(width, height);
                using var canvas = new MagickImage(new MagickColor(options.Background), (uint)width, (uint)height);
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    using var frame = images[checked(options.FirstFrame + index * options.FrameStep)].Clone();
                    frame.Resize((uint)options.ThumbnailWidth, (uint)options.ThumbnailHeight);
                    canvas.Composite(frame, index % columns * options.ThumbnailWidth +
                                            (options.ThumbnailWidth - (int)frame.Width) / 2,
                        index / columns * options.ThumbnailHeight +
                        (options.ThumbnailHeight - (int)frame.Height) / 2, CompositeOperator.Over);
                }

                return await Write(canvas, output, null, 90, true, options.Overwrite, token);
            }, token);
        }

        private static async Task<MediaInfo> Run(Func<Task<MediaInfo>> action, CancellationToken token)
        {
            await Gate.WaitAsync(token);
            try
            {
                return await Task.Run(action, token);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<MagickImageCollection> Read(string path, CancellationToken token)
        {
            var info = await _media.InspectAsync(path, token);
            Budget(info.Width, info.Height, info.Frames);
            if (info.Bytes > PixelBudget) throw new InvalidOperationException(UiText.Get("MemoryLimit"));
            var images = info.Format is "png" or "apng"
                ? MediaPngCodec.ReadImages(path)
                : new MagickImageCollection(path);
            try
            {
                token.ThrowIfCancellationRequested();
                if (images.Count != info.Frames || images.Count == 0 ||
                    images.Max(image => Math.Max(image.Width, image.Page.Width)) != info.Width ||
                    images.Max(image => Math.Max(image.Height, image.Page.Height)) != info.Height)
                    throw new InvalidDataException();
                foreach (var image in images)
                    image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                if (info.Format == "apng") MediaPngCodec.Coalesce(images, token);
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

        private async Task<MediaInfo> Write(MagickImage canvas, string output, string? format, int quality,
            bool lossless, bool overwrite, CancellationToken token)
        {
            output = Path.GetFullPath(output);
            format = (format ?? Path.GetExtension(output).TrimStart('.')).ToLowerInvariant();
            if (quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(quality));
            var target = format switch
            {
                "png" => MagickFormat.Png, "webp" => MagickFormat.WebP, "gif" => MagickFormat.Gif,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            if (!overwrite && File.Exists(output)) throw new IOException(output);
            var directory = Path.GetDirectoryName(output)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            try
            {
                canvas.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                canvas.Quality = (uint)quality;
                canvas.Settings.SetDefine(MagickFormat.WebP, "lossless", lossless);
                token.ThrowIfCancellationRequested();
                canvas.Write(temporary, target);
                var result = await _media.InspectAsync(temporary, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, output, overwrite);
                return result with { Path = output };
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void Budget(int width, int height, int frames = 1)
        {
            if (width <= 0 || height <= 0 || frames <= 0 || (double)width * height * frames * 8 > PixelBudget)
                throw new InvalidOperationException(UiText.Get("MemoryLimit"));
        }

        private static void ApplyEffects(IMagickImage<byte> image, IReadOnlyList<PixelEffect> effects,
            PixelEffectSpace space, CancellationToken token)
        {
            if (effects.Count > 32) throw new ArgumentOutOfRangeException(nameof(effects));
            foreach (var effect in effects) effect.Validate();
            var selected = effects.Where(effect => effect.Space == space).ToArray();
            if (selected.Length == 0) return;
            byte[] pixels;
            using (var source = image.GetPixels()) pixels = source.ToByteArray(PixelMapping.BGRA)!;
            PixelEffectProcessor.Apply(pixels, checked((int)image.Width), checked((int)image.Height), selected, token);
            image.ReadPixels(pixels,
                new PixelReadSettings(image.Width, image.Height, StorageType.Char, PixelMapping.BGRA));
        }
    }
}
