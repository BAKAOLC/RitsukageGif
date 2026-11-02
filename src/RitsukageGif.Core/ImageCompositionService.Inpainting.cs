using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed partial class ImageCompositionService
    {
        public Task<MediaInfo> InpaintAsync(string input, string output, ImageInpaintingOptions options,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            Logger.ZLogInformation(
                $"Image inpainting requested: input={input}, frameIndex={options.FrameIndex}, radius={options.Radius}");
            return Run(async () =>
            {
                if (options.Radius is < 1 or > 32 || options.Quality is < 1 or > 100 ||
                    options.Regions.Count > 128 || !Enum.IsDefined(options.MaskChannel))
                    throw new ArgumentOutOfRangeException(nameof(options));
                input = Path.GetFullPath(input);
                output = Path.GetFullPath(output);
                var info = await _media.InspectAsync(input, token);
                Budget(info.Width, info.Height, 3);
                if (options.FrameIndex < 0 || options.FrameIndex >= info.Frames)
                    throw new ArgumentOutOfRangeException(nameof(options));
                var format = (options.Format ?? Path.GetExtension(output).TrimStart('.')).ToLowerInvariant();
                if (format is not ("png" or "webp" or "gif" or "jpg" or "jpeg" or "bmp"))
                    throw new ArgumentOutOfRangeException(nameof(options));
                if (!options.Overwrite && File.Exists(output)) throw new IOException(output);
                var coverage = await ReadInpaintingMask(info.Width, info.Height, options, token);
                if (info.Format is "png" or "apng" && ReadPngDepth(input) == 16)
                {
                    var raster = info.Format == "png"
                        ? PngImageCodec.Decode(await File.ReadAllBytesAsync(input, token), token)
                        : PngAnimation.ReadHighPrecisionFrame(input, options.FrameIndex, token);
                    var restored = ImageInpainting.RestorePng(raster, coverage, options.Radius, token);
                    if (format == "png")
                        return await WriteInpaintingPng(PngImageCodec.Encode(restored, token), output,
                            options.Overwrite, token);
                    var preview = ImageInpainting.ToSdrBgra(restored, token);
                    using var converted = new MagickImage(preview,
                        new PixelReadSettings((uint)restored.Width, (uint)restored.Height, StorageType.Char,
                            PixelMapping.BGRA));
                    return await WriteInpaintingImage(converted, output, format, options, token);
                }

                using var image = await ReadInpaintingFrame(input, info, options.FrameIndex, token);
                NormalizeSdrWorkingImage(image);
                image.Alpha(AlphaAction.Set);
                byte[] pixels;
                using (var values = image.GetPixels()) pixels = values.ToByteArray(PixelMapping.BGRA)!;
                ImageInpainting.RestoreBgra(pixels, info.Width, info.Height, coverage, options.Radius, token);
                image.ReadPixels(pixels, new PixelReadSettings((uint)info.Width, (uint)info.Height,
                    StorageType.Char, PixelMapping.BGRA));
                image.SetProfile(ColorProfiles.SRGB);
                return await WriteInpaintingImage(image, output, format, options, token);
            }, token);
        }

        private async Task<IMagickImage<byte>> ReadInpaintingFrame(string input, MediaInfo info, int frameIndex,
            CancellationToken token)
        {
            var frames = info switch
            {
                { Format: "apng" } => MediaPngCodec.ReadCoalescedFrames(input, info, token),
                { Format: "webp", Frames: > 1 } => MediaWebpCodec.ReadCoalescedFrames(input, info, token),
                { Format: "gif", Frames: > 1 } => AnimationEditDocument.ReadAnimationFrames(input, info, token),
                _ => null,
            };
            if (frames is null)
            {
                using var images = await Read(input, token);
                return MagickImageCloner.Clone(images[frameIndex], token);
            }

            var index = 0;
            foreach (var frame in frames)
            {
                token.ThrowIfCancellationRequested();
                if (index++ == frameIndex) return MagickImageCloner.Clone(frame, token);
            }

            throw new InvalidDataException("The selected image frame was not found.");
        }

        private async Task<ushort[]> ReadInpaintingMask(int width, int height, ImageInpaintingOptions options,
            CancellationToken token)
        {
            if (options.Regions.Count == 0 && options.MaskPath is null)
                throw new ArgumentException("An inpainting mask or region is required.", nameof(options));
            var result = new ushort[checked(width * height)];
            if (options.MaskPath is { } path)
            {
                var info = await _media.InspectAsync(path, token);
                if (info.Width != width || info.Height != height)
                    throw new ArgumentException("The inpainting mask must match the input image dimensions.",
                        nameof(options));
                using var image = await ReadInpaintingFrame(path, info, 0, token);
                image.Alpha(AlphaAction.Set);
                using var pixels = image.GetPixels();
                var values = pixels.ToByteArray(PixelMapping.BGRA)!;
                for (var index = 0; index < result.Length; index++)
                {
                    if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                    var offset = index * 4;
                    var intensity = options.MaskChannel == ImageMaskChannel.Alpha
                        ? 255d
                        : .2126 * values[offset + 2] + .7152 * values[offset + 1] + .0722 * values[offset];
                    var samples = (int)Math.Clamp(Math.Round(intensity * values[offset + 3] * 16 / (255 * 255)), 0, 16);
                    result[index] = (ushort)((1u << samples) - 1);
                }
            }

            if (options.Regions.Count == 0) return result;
            var bounded = options.MaskPath is null ? result : new ushort[result.Length];
            var bounds = new PixelRect(0, 0, width, height);
            foreach (var region in options.Regions)
            {
                if (region.IsEmpty || region.Intersect(bounds) != region)
                    throw new ArgumentOutOfRangeException(nameof(options));
                for (var y = region.Y; y < region.Bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    if (options.MaskPath is null)
                        Array.Fill(bounded, ushort.MaxValue, y * width + region.X, region.Width);
                    else result.AsSpan(y * width + region.X, region.Width).CopyTo(bounded.AsSpan(y * width + region.X));
                }
            }

            return bounded;
        }

        private Task<MediaInfo> WriteInpaintingImage(IMagickImage<byte> image, string output, string format,
            ImageInpaintingOptions options, CancellationToken token)
        {
            return Write(image, output, format, options.Quality, options.Lossless, options.Overwrite, token);
        }

        private async Task<MediaInfo> WriteInpaintingPng(byte[] png, string output, bool overwrite,
            CancellationToken token)
        {
            var directory = Path.GetDirectoryName(output)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, png, token);
                var info = await _media.InspectAsync(temporary, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, output, overwrite);
                return info with { Path = output };
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static byte ReadPngDepth(string path)
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[25];
            stream.ReadExactly(header);
            return header[24];
        }
    }
}
