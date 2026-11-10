using Avalonia.Threading;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationScriptService(
        string? baseDirectory = null,
        Func<CancellationToken, Task<byte[]>>? capture = null) : IAsyncDisposable
    {
        private readonly string _directory = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
        private readonly HashSet<AnnotationScriptCanvas> _canvases = [];
        private readonly CancellationTokenSource _lifetime = new();
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _lifetime.CancelAsync();
            var canvases = await Dispatcher.UIThread.InvokeAsync(() => _canvases.ToArray());
            List<Exception>? failures = null;
            try
            {
                foreach (var canvas in canvases)
                    try
                    {
                        await canvas.DisposeAsync();
                    }
                    catch (Exception error)
                    {
                        (failures ??= []).Add(error);
                    }
            }
            finally
            {
                _lifetime.Dispose();
            }

            if (failures is not null) throw new AggregateException(failures);
        }

        internal string Resolve(string path)
        {
            return Path.GetFullPath(path, _directory);
        }

        internal async Task<AnnotationScriptCanvas> OpenAsync(object source, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var encoded = await ReadAsync(source, linked.Token);
            return await DecodeAsync(encoded, linked.Token);
        }

        internal async Task<AnnotationScriptCanvas> CaptureAsync(CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var callback = capture ?? throw new InvalidOperationException("No capture source is available.");
            return await DecodeAsync(await callback(linked.Token), linked.Token);
        }

        internal async Task<AnnotationScriptCanvas> CreateAsync(int width, int height, string color,
            CancellationToken token)
        {
            ValidateDimensions(width, height);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var png = await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var image = new MagickImage(new MagickColor(color), (uint)width, (uint)height);
                return image.ToByteArray(MagickFormat.Png);
            }, linked.Token);
            return await DecodeAsync(png, linked.Token);
        }

        internal async Task<ReadOnlyMemory<byte>> ReadAsync(object source, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (source is ScriptBytes bytes) return bytes.Data;
            if (source is not string location) throw new ArgumentException("An image path or Bytes value is required.");
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.IsFile)
                location = Resolve(uri?.IsFile == true ? uri.LocalPath : location);
            using var resource = await MediaResource.OpenAsync(location, token);
            return await File.ReadAllBytesAsync(resource.Path, token);
        }

        private async Task<AnnotationScriptCanvas> DecodeAsync(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            var decoded = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var image = new MagickImage();
                image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                var settings = new MagickReadSettings { FrameCount = 1 };
                image.Ping(encoded.Span, settings);
                ValidateDimensions(checked((int)image.Width), checked((int)image.Height));
                var hdr = HdrImageDecoder.TryDecode(encoded, token: token);
                if (hdr is not null)
                {
                    ValidateDimensions(hdr.Width, hdr.Height);
                    var sdr = HdrToneMapping.Map(hdr, token: token);
                    var hdrPreview = CapturedFramePixels.ToStraightBgra(sdr, token);
                    using var preview = new MagickImage();
                    preview.ReadPixels(hdrPreview, new PixelReadSettings((uint)hdr.Width, (uint)hdr.Height,
                        StorageType.Char, PixelMapping.BGRA));
                    preview.ColorSpace = ColorSpace.sRGB;
                    return (width: hdr.Width, height: hdr.Height, png: preview.ToByteArray(MagickFormat.Png),
                        bgra: hdrPreview, hdr);
                }

                image.Read(encoded.Span, settings);
                var preservePng = image is
                                  {
                                      Format: MagickFormat.Png,
                                      Orientation: Orientation.Undefined or Orientation.TopLeft,
                                  } &&
                                  (encoded.Span[24] == 16 || image.GetColorProfile() is null);
                image.AutoOrient();
                var width = checked((int)image.Width);
                var height = checked((int)image.Height);
                ValidateDimensions(width, height);
                if (image.GetColorProfile() is not null && !image.TransformColorSpace(ColorProfiles.SRGB))
                    throw new InvalidDataException("The image color profile could not be converted.");
                image.ColorSpace = ColorSpace.sRGB;
                image.ColorType = ColorType.TrueColorAlpha;
                using var pixels = image.GetPixels();
                var bgra = pixels.ToByteArray(PixelMapping.BGRA)!;
                var png = preservePng ? encoded.ToArray() : image.ToByteArray(MagickFormat.Png);
                return (width, height, png, bgra, hdr);
            }, token);
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                var canvas = new AnnotationScriptCanvas(this, decoded.width, decoded.height, decoded.png, decoded.bgra,
                    decoded.hdr);
                _canvases.Add(canvas);
                return canvas;
            });
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0 || (double)width * height * 4 > 256L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(width),
                    "The image exceeds the existing static-image pixel budget.");
        }

        internal AnnotationScriptCanvas Attach(ScreenshotAnnotationSession session, int width, int height,
            Func<CancellationToken, Task<byte[]>> composeOutput, Func<bool> available,
            Func<CancellationToken, Task<CapturedFrame?>>? composeHdrOutput = null)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDimensions(width, height);
            var canvas = new AnnotationScriptCanvas(this, session, width, height, composeOutput, available,
                composeHdrOutput);
            _canvases.Add(canvas);
            return canvas;
        }

        internal void Forget(AnnotationScriptCanvas canvas)
        {
            _canvases.Remove(canvas);
        }
    }
}
