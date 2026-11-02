using System.Buffers;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowRoiPinFrame : IDisposable
    {
        private readonly PreviewFrame _storage;

        internal WindowRoiPinFrame(int width, int height, byte[] pixels, CapturedFrame? hdr)
        {
            var sdr = ArrayPool<byte>.Shared.Rent(checked(width * height * 4));
            byte[]? linear = null;
            try
            {
                pixels.AsSpan(0, width * height * 4).CopyTo(sdr);
                if (hdr is not null)
                {
                    linear = ArrayPool<byte>.Shared.Rent(checked(width * height * 8));
                    for (var row = 0; row < height; row++)
                        hdr.Pixels.Span.Slice(row * hdr.Stride, width * 8)
                            .CopyTo(linear.AsSpan(row * width * 8, width * 8));
                }

                _storage = new(sdr, width, height, 0, linear,
                    hdr?.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits ?? 1,
                    hdr?.AlphaMode ?? CaptureAlphaMode.Opaque);
                Hdr = hdr is null
                    ? null
                    : hdr with
                    {
                        Stride = width * 8, Pixels = linear!.AsMemory(0, width * height * 8),
                    };
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(sdr);
                if (linear is not null) ArrayPool<byte>.Shared.Return(linear);
                throw;
            }
        }

        private WindowRoiPinFrame(PreviewFrame storage, CapturedFrame? hdr, double? nativePixelDensity = null)
        {
            _storage = storage;
            Hdr = hdr;
            NativePixelDensity = nativePixelDensity;
        }

        public int Width => _storage.Width;
        public int Height => _storage.Height;
        public byte[] Pixels => _storage.Pixels;
        public CapturedFrame? Hdr { get; }
        internal long StorageBytes => _storage.StorageBytes;
        internal double? NativePixelDensity { get; }

        public void Dispose()
        {
            _storage.Dispose();
        }

        internal static WindowRoiPinFrame FromPooled(int width, int height, byte[] pixels, byte[]? linear,
            CapturedFrame? hdr, double? nativePixelDensity = null)
        {
            return new(new(pixels, width, height, 0, linear,
                hdr?.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits ?? 1,
                hdr?.AlphaMode ?? CaptureAlphaMode.Opaque), hdr, nativePixelDensity);
        }

        internal WindowRoiPinFrame Retain()
        {
            return new(_storage.Retain(), Hdr, NativePixelDensity);
        }

        internal void UpdateSurface(AnimationPinSurface surface)
        {
            surface.Update(_storage);
        }

        internal WindowRoiPinFrame Transform(PinnedImageTransform transform, CancellationToken token)
        {
            using var frame = Retain();
            var bounds = new PixelRect(0, 0, Width, Height);
            var descriptor = new WindowRoiPinDescriptor(1, 1, "", "", bounds).Transform(transform);
            var source = new CapturedFrame(Width, Height, Width * 4, CapturePixelFormat.Bgra8Srgb,
                frame.Pixels.AsMemory(0, checked(Width * Height * 4)),
                new(false), CaptureAlphaMode.Straight, 0, []);
            return WindowRoiPinSource.CreateFrame(new(source, frame.Hdr, 0), bounds, descriptor, token,
                nativePixelDensity: NativePixelDensity);
        }

        internal byte[] EncodePng(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            using var storage = _storage.Retain();
            using var image = new MagickImage();
            image.ReadPixels(storage.Pixels.AsSpan(0, checked(Width * Height * 4)),
                new PixelReadSettings((uint)Width, (uint)Height, StorageType.Char, PixelMapping.BGRA));
            image.ColorSpace = ColorSpace.sRGB;
            image.Progress += (_, args) => args.Cancel = token.IsCancellationRequested;
            try
            {
                var encoded = image.ToByteArray(MagickFormat.Png32);
                token.ThrowIfCancellationRequested();
                return encoded;
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        }

        internal async Task<PinnedImageContent> PrepareContentAsync(CancellationToken token)
        {
            using var frame = Retain();
            // ReSharper disable once AccessToDisposedClosure
            var png = await Task.Run(() => frame.EncodePng(token), token).ConfigureAwait(false);
            var content = await Task.Run(() => PinnedImageContent.Decode(png, token), token).ConfigureAwait(false);
            if (frame.Hdr is not { } borrowed) return content;
            var hdr = borrowed with { Pixels = borrowed.Pixels.ToArray() };
            var encoded = await HdrImageService.EncodePngAsync(hdr, token).ConfigureAwait(false);
            return PinnedImageContent.FromHdr(encoded, content, hdr);
        }
    }
}
