using ImageMagick;

namespace RitsukageGif.Core
{
    // Instances are created by Open/Create in the main partial declaration.
    // ReSharper disable once ClassCannotBeInstantiated
    public sealed partial class AnimationEditDocument
    {
        private List<AnimationEditFrame> Read(string path, bool fit, CancellationToken token, out uint iterations)
        {
            var info = new MediaProcessingService().InspectAsync(path, token).GetAwaiter().GetResult();
            ValidateSize(info.Width, info.Height);
            var frames = new List<AnimationEditFrame>();
            uint loops = 0;

            if (info.Format == "apng")
            {
                foreach (var image in MediaPngCodec.ReadCoalescedFrames(path, info, token)) Add(image);
            }
            else
            {
                using var images = MediaProcessingService.Decode(path, info, token);
                foreach (var image in images) Add(image);
            }

            if (frames.Count == 0) throw new InvalidDataException();
            iterations = loops;
            return frames;

            void Add(IMagickImage<byte> image)
            {
                token.ThrowIfCancellationRequested();
                loops = image.AnimationIterations;
                var ticks = image.AnimationTicksPerSecond == 0 ? 100 : image.AnimationTicksPerSecond;
                var delay = Math.Max(1, checked((int)Math.Round(image.AnimationDelay * 1000d / ticks)));
                if (info.Frames == 1 && image.AnimationDelay == 0) delay = 100;
                if (fit && (image.Width != Width || image.Height != Height))
                {
                    image.Resize(new MagickGeometry((uint)Width, (uint)Height));
                    image.BackgroundColor = MagickColors.Transparent;
                    image.Extent((uint)Width, (uint)Height, Gravity.Center);
                }

                frames.Add(Store(image, delay, token));
            }
        }

        private AnimationEditFrame Store(IMagickImage<byte> image, int delayMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".png");
            image.ResetPage();
            image.Write(path, MagickFormat.Png32);
            token.ThrowIfCancellationRequested();
            return new(path, checked((int)image.Width), checked((int)image.Height), delayMs);
        }

        private void Edit(IEnumerable<int> indices, Action<MagickImage> edit, CancellationToken token)
        {
            var selected = Select(indices);
            var frames = Frames.ToArray();
            try
            {
                foreach (var index in selected)
                {
                    token.ThrowIfCancellationRequested();
                    using var image = new MagickImage(frames[index].Path);
                    image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                    edit(image);
                    frames[index] = Store(image, frames[index].DelayMs, token);
                }

                token.ThrowIfCancellationRequested();
                Commit(frames);
            }
            catch (Exception error) when (token.IsCancellationRequested &&
                                          error is MagickException or ObjectDisposedException)
            {
                throw new OperationCanceledException(token);
            }
            finally
            {
                Prune();
            }
        }

        public void Transform(MediaProcessOptions options, CancellationToken token = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(options);
            var crop = options.CropX.HasValue || options.CropY.HasValue || options.CropWidth.HasValue ||
                       options.CropHeight.HasValue;
            if ((crop && (options.CropX is null or < 0 || options.CropY is null or < 0 ||
                          options.CropWidth is null or <= 0 || options.CropHeight is null or <= 0 ||
                          (long)options.CropX + options.CropWidth > Width ||
                          (long)options.CropY + options.CropHeight > Height)) ||
                options.Rotation is not (0 or 90 or 180 or 270))
                throw new ArgumentOutOfRangeException(nameof(options));
            var width = options.CropWidth ?? Width;
            var height = options.CropHeight ?? Height;
            if (options.Rotation is 90 or 270) (width, height) = (height, width);
            var targetWidth = options.Width ?? (options.Height.HasValue
                ? checked((int)Math.Max(1, Math.Round(width * (double)options.Height.Value / height)))
                : width);
            var targetHeight = options.Height ?? (options.Width.HasValue
                ? checked((int)Math.Max(1, Math.Round(height * (double)options.Width.Value / width)))
                : height);
            ValidateSize(targetWidth, targetHeight);
            Edit(Enumerable.Range(0, Frames.Count), image =>
            {
                if (crop)
                    image.Crop(new MagickGeometry(options.CropX!.Value, options.CropY!.Value,
                        (uint)options.CropWidth!.Value, (uint)options.CropHeight!.Value));
                image.ResetPage();
                if (options.FlipHorizontal) image.Flop();
                if (options.FlipVertical) image.Flip();
                if (options.Rotation != 0) image.Rotate(options.Rotation);
                image.Resize(new MagickGeometry((uint)targetWidth, (uint)targetHeight) { IgnoreAspectRatio = true });
                if (options.Effects is { Count: > 0 }) ProcessEffects(image, options.Effects, token);
            }, token);
        }

        public void ApplyEffects(IEnumerable<int> indices, IReadOnlyList<PixelEffect> effects,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(effects);
            Edit(indices, image => ProcessEffects(image, effects, token), token);
        }

        private static void ProcessEffects(MagickImage image, IReadOnlyList<PixelEffect> effects,
            CancellationToken token)
        {
            byte[] pixels;
            using (var collection = image.GetPixels()) pixels = collection.ToByteArray(PixelMapping.BGRA)!;
            PixelEffectProcessor.Apply(pixels, checked((int)image.Width), checked((int)image.Height), effects,
                token: token);
            image.ReadPixels(pixels,
                new PixelReadSettings(image.Width, image.Height, StorageType.Char, PixelMapping.BGRA));
        }

        public void Overlay(IEnumerable<int> indices, string overlayPng, int x = 0, int y = 0,
            CancellationToken token = default)
        {
            using var overlay = new MagickImage(overlayPng);
            // Edit invokes the callback synchronously before the overlay is disposed.
            // ReSharper disable once AccessToDisposedClosure
            Edit(indices, image => image.Composite(overlay, x, y, CompositeOperator.Over), token);
        }

        public void ReplaceFrames(IEnumerable<int> indices, byte[] png, CancellationToken token = default)
        {
            ThrowIfDisposed();
            using var replacement = new MagickImage(png);
            if (replacement.Width != Width || replacement.Height != Height)
                throw new ArgumentException(null, nameof(png));
            // Edit invokes the callback synchronously before the replacement is disposed.
            // ReSharper disable once AccessToDisposedClosure
            Edit(indices, image => image.Composite(replacement, CompositeOperator.Src), token);
        }
    }
}
