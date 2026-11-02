using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed class PortableFrameComposer : IDisposable
    {
        private readonly int _width, _height;
        private readonly bool _retainHdr;
        private readonly Dictionary<int, SKBitmap> _sdrSources = [], _hdrSources = [];
        private readonly SKBitmap _sdr;
        private readonly SKCanvas _sdrCanvas;
        private readonly SKBitmap? _hdr;
        private readonly SKCanvas? _hdrCanvas;
        private readonly SKPaint _paint = new() { IsAntialias = true };
        private readonly List<FrameColorRegion> _regions = [];
        private bool _hasHdr, _hasLinearPlane;
        private long _revision, _timestamp;

        public PortableFrameComposer(int width, int height, bool retainHdr)
        {
            _width = width;
            _height = height;
            _retainHdr = retainHdr;
            using var srgb = SKColorSpace.CreateSrgb();
            _sdr = new(new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb));
            _sdrCanvas = new(_sdr);
            if (retainHdr)
            {
                using var linear = SKColorSpace.CreateSrgbLinear();
                _hdr = new(new(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, linear));
                _hdrCanvas = new(_hdr);
            }
        }

        public void Dispose()
        {
            foreach (var item in _sdrSources.Values) item.Dispose();
            foreach (var item in _hdrSources.Values) item.Dispose();
            _paint.Dispose();
            _sdrCanvas.Dispose();
            _hdrCanvas?.Dispose();
            _sdr.Dispose();
            _hdr?.Dispose();
        }

        public void Begin()
        {
            _sdrCanvas.Clear(SKColors.Transparent);
            _hdrCanvas?.Clear(SKColors.Transparent);
            _regions.Clear();
            _hasHdr = false;
            _hasLinearPlane = false;
            _revision = 0;
            _timestamp = 0;
        }

        public void Draw(NativeCaptureSnapshot snapshot, PixelRect bounds, PixelRect region, int index,
            bool includeCursor = false, CancellationToken token = default)
        {
            if (includeCursor != snapshot.CursorEmbedded && snapshot.Capabilities is { SeparateCursor: false })
                throw new NotSupportedException(UiText.Get("CaptureCursorReselect"));
            var clipped = bounds.Intersect(region);
            if (clipped.IsEmpty) return;
            var frames = snapshot.Frames;
            DrawPlane(_sdrCanvas, _sdrSources, frames.Sdr, bounds, region, index, false, token);
            if (_retainHdr)
            {
                var source = frames.Hdr ?? frames.Sdr;
                DrawPlane(_hdrCanvas!, _hdrSources, source, bounds, region, index, true, token);
                _hasLinearPlane |= frames.Hdr is not null;
                _hasHdr |= source.ColorInfo.IsHdrSource || source.ColorRegions.Any(part => part.ColorInfo.IsHdrSource);
            }

            if (includeCursor && snapshot is
                {
                    Capabilities.SeparateCursor: true, Cursor: { Valid: true, Visible: true } cursor,
                    CursorPixels: { } cursorPixels,
                })
            {
                var cursorBounds = new PixelRect((int)Math.Floor(cursor.X - cursor.HotspotX / cursor.PixelScale),
                    (int)Math.Floor(cursor.Y - cursor.HotspotY / cursor.PixelScale),
                    Math.Max(1, (int)Math.Ceiling(cursor.Width / cursor.PixelScale)),
                    Math.Max(1, (int)Math.Ceiling(cursor.Height / cursor.PixelScale)));
                var cursorFrame = new CapturedFrame(cursor.Width, cursor.Height, cursor.Width * 4,
                    CapturePixelFormat.Bgra8Srgb, cursorPixels, (frames.Hdr ?? frames.Sdr).ColorInfo,
                    CaptureAlphaMode.Straight, frames.Sdr.Revision, []);
                DrawPlane(_sdrCanvas, _sdrSources, cursorFrame, cursorBounds, region, -index - 1, false, token);
                if (_retainHdr)
                    DrawPlane(_hdrCanvas!, _hdrSources, cursorFrame, cursorBounds, region, -index - 1, true, token);
            }

            _revision = Math.Max(_revision, frames.Sdr.Revision);
            _timestamp = Math.Max(_timestamp, frames.TimestampTicks);
            var reference = frames.Hdr ?? frames.Sdr;
            if (reference.ColorRegions.Count == 0)
                AddRegion(clipped.X, clipped.Y, clipped.Right, clipped.Bottom, reference.ColorInfo);
            else
                foreach (var part in reference.ColorRegions)
                {
                    var sourceArea = part.Region.Intersect(new(0, 0, reference.Width, reference.Height));
                    if (sourceArea.IsEmpty) continue;
                    var left = Math.Max(clipped.X, bounds.X + (double)sourceArea.X * bounds.Width / reference.Width);
                    var top = Math.Max(clipped.Y, bounds.Y + (double)sourceArea.Y * bounds.Height / reference.Height);
                    var right = Math.Min(clipped.Right,
                        bounds.X + (double)sourceArea.Right * bounds.Width / reference.Width);
                    var bottom = Math.Min(clipped.Bottom,
                        bounds.Y + (double)sourceArea.Bottom * bounds.Height / reference.Height);
                    AddRegion(left, top, right, bottom, part.ColorInfo);
                }

            return;

            void AddRegion(double left, double top, double right, double bottom, FrameColorInfo color)
            {
                if (right <= left || bottom <= top) return;
                var outputLeft = Math.Clamp((int)Math.Floor((left - region.X) * _width / region.Width), 0, _width);
                var outputTop = Math.Clamp((int)Math.Floor((top - region.Y) * _height / region.Height), 0, _height);
                var outputRight = Math.Clamp((int)Math.Ceiling((right - region.X) * _width / region.Width), 0, _width);
                var outputBottom = Math.Clamp((int)Math.Ceiling((bottom - region.Y) * _height / region.Height), 0,
                    _height);
                if (outputRight > outputLeft && outputBottom > outputTop)
                    _regions.Add(new(new(outputLeft, outputTop, outputRight - outputLeft, outputBottom - outputTop),
                        color));
            }
        }

        private unsafe void DrawPlane(SKCanvas canvas, Dictionary<int, SKBitmap> cache, CapturedFrame frame,
            PixelRect bounds, PixelRect region, int index, bool linear, CancellationToken token)
        {
            var format = linear ? SKColorType.RgbaF16 : SKColorType.Bgra8888;
            if (!cache.TryGetValue(index, out var bitmap) || bitmap.Width != frame.Width ||
                bitmap.Height != frame.Height)
            {
                bitmap?.Dispose();
                using var space = linear ? SKColorSpace.CreateSrgbLinear() : SKColorSpace.CreateSrgb();
                cache[index] = bitmap = new(new(frame.Width, frame.Height, format,
                    linear ? SKAlphaType.Premul : SKAlphaType.Unpremul, space));
            }

            for (var y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var target = new Span<byte>((byte*)bitmap.GetPixels() + y * bitmap.RowBytes,
                    frame.Width * (linear ? 8 : 4));
                var input = frame.Pixels.Span.Slice(y * frame.Stride, frame.Width *
                                                                      (frame.PixelFormat ==
                                                                       CapturePixelFormat.Rgba16FloatScRgb
                                                                          ? 8
                                                                          : 4));
                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (!linear && frame.AlphaMode != CaptureAlphaMode.Premultiplied)
                {
                    input.CopyTo(target);
                    if (frame.AlphaMode == CaptureAlphaMode.Opaque)
                        for (var alpha = 3; alpha < target.Length; alpha += 4)
                            target[alpha] = 255;
                }
                else if (linear && frame.PixelFormat == CapturePixelFormat.Rgba16FloatScRgb)
                {
                    input.CopyTo(target);
                    if (frame.AlphaMode != CaptureAlphaMode.Premultiplied)
                    {
                        var values = MemoryMarshal.Cast<byte, Half>(target);
                        for (var x = 0; x < values.Length; x += 4)
                        {
                            var alpha = frame.AlphaMode == CaptureAlphaMode.Opaque ? 1 : (float)values[x + 3];
                            for (var c = 0; c < 3; c++) values[x + c] = (Half)((float)values[x + c] * alpha);
                            values[x + 3] = (Half)alpha;
                        }
                    }
                }
                else
                {
                    var values = linear ? MemoryMarshal.Cast<byte, Half>(target) : default;
                    for (var x = 0; x < frame.Width; x++)
                    {
                        var offset = x * 4;
                        var alpha = frame.AlphaMode == CaptureAlphaMode.Opaque ? 1 : input[offset + 3] / 255f;
                        var divisor = frame.AlphaMode == CaptureAlphaMode.Premultiplied ? Math.Max(alpha, 1 / 255f) : 1;
                        for (var c = 0; c < 3; c++)
                        {
                            var value = Math.Clamp(input[offset + c] / 255f / divisor, 0, 1);
                            if (linear)
                            {
                                var decoded = value <= .04045f
                                    ? value / 12.92f
                                    : MathF.Pow((value + .055f) / 1.055f, 2.4f);
                                values[offset + 2 - c] = (Half)(decoded * alpha * frame.ColorInfo.SdrWhiteNits / 80);
                            }
                            else
                            {
                                target[offset + c] = (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
                            }
                        }

                        if (linear) values[offset + 3] = (Half)alpha;
                        else target[offset + 3] = (byte)MathF.Round(alpha * 255);
                    }
                }
            }

            bitmap.NotifyPixelsChanged();
            CaptureFrameDrawing.Draw(canvas, _paint, bitmap, bounds, region, _width, _height);
        }

        public CapturedFrameSet Finish(CancellationToken token)
        {
            var sdr = Read(_sdr, false, token);
            var hdr = _hasLinearPlane && _hdr is not null ? Read(_hdr, true, token) : null;
            return new(sdr, hdr, _timestamp);
        }

        private unsafe CapturedFrame Read(SKBitmap bitmap, bool hdr, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var bytes = new byte[checked(_width * _height * (hdr ? 8 : 4))];
            using var pixmap = bitmap.PeekPixels();
            var info = bitmap.Info.WithAlphaType(hdr ? SKAlphaType.Premul : SKAlphaType.Unpremul);
            fixed (byte* address = bytes)
                if (!pixmap.ReadPixels(info, (nint)address, info.RowBytes, 0, 0))
                    throw new IOException(UiText.Get("InvalidFrameBuffer"));
            return new(_width, _height, info.RowBytes,
                hdr ? CapturePixelFormat.Rgba16FloatScRgb : CapturePixelFormat.Bgra8Srgb, bytes,
                new(_hasHdr), hdr ? CaptureAlphaMode.Premultiplied : CaptureAlphaMode.Straight,
                _revision, [.. _regions]);
        }
    }
}
