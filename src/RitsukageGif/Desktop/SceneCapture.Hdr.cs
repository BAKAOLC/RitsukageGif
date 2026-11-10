using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture
    {
        private static readonly SKColorSpace HdrColorSpace = SKColorSpace.CreateSrgbLinear();
        private readonly Dictionary<Layer, HdrLayer> _hdrLayers = [];
        private SKBitmap? _hdrCanvas;
        private SKCanvas? _hdrDrawing;
        private long _hdrRevision, _hdrTimestamp;

        public CapturedFrameSet CaptureFrames(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ownsContext) _context!.BeginFrame(_clock.Elapsed.TotalSeconds);
            _topology = _context?.GetTopology(_displayProvider) ?? _displayProvider();
            _frame = _runtime is null ? _staticFrame : _context?.GetSnapshot(_runtime) ?? _runtime.Snapshot;
            _frameMotionTime = _context?.MotionTime ?? PresetMotion.Now;
            if (_needsWindowTargets)
                _windows = _portalSource?.Session.GetAuthorizedWindows() ??
                           _context?.GetWindows() ?? WindowLocator.List();
            _hdrTimestamp = Stopwatch.GetTimestamp();
            foreach (var layer in _layers)
            {
                var state = _frame.Layers.GetValueOrDefault(layer.Definition.Name);
                var placement = state?.Placement ?? layer.Definition.Placement;
                layer.EnsureResolution(Math.Max(placement.Width, state?.Motion?.From.Width ?? 0),
                    Math.Max(placement.Height, state?.Motion?.From.Height ?? 0), _options.Divisor,
                    _options.PixelDensity);
                if (!_hdrLayers.TryGetValue(layer, out var hdr) || hdr.Bitmap.Width != layer.Bitmap.Width ||
                    hdr.Bitmap.Height != layer.Bitmap.Height)
                {
                    hdr?.Dispose();
                    _hdrLayers[layer] = hdr = new(layer.Bitmap.Width, layer.Bitmap.Height);
                }

                hdr.Rendered = false;
                hdr.IsHdrSource = false;
            }

            _hdrCanvas ??= CreateHdrBitmap(_options.Width, _options.Height);
            _hdrDrawing ??= new(_hdrCanvas);
            ResetInputGeometry();
            _hdrDrawing.Clear(LinearColor(_background));
            using var paint = new SKPaint();
            paint.IsAntialias = true;
            var isHdr = false;
            foreach (var layer in _layers)
            {
                token.ThrowIfCancellationRequested();
                var state = _frame.Layers.GetValueOrDefault(layer.Definition.Name);
                if (state?.Visible == false) continue;
                var hdr = RenderHdrLayer(layer, token);
                var placement = state?.Motion?.At(_frameMotionTime) ?? state?.Placement ?? layer.Definition.Placement;
                paint.ColorF = new(1, 1, 1, (float)Math.Clamp(state?.Opacity ?? 1, 0, 1));
                var scale = (float)(_options.PixelDensity / _options.Divisor);
                _hdrDrawing.DrawSampledBitmap(hdr.Bitmap, new(placement.X * scale, placement.Y * scale,
                    placement.Right * scale, placement.Bottom * scale), paint);
                isHdr |= hdr.IsHdrSource;
            }

            _hdrDrawing.Flush();
            var result = ReadHdrBitmap(_hdrCanvas, new(isHdr), ++_hdrRevision);
            var sdr = HdrToneMapping.Map(result, token: token);
            if (_ownsContext && !_clock.IsRunning) _clock.Start();
            return new(sdr, result, _hdrTimestamp);
        }

        private HdrLayer RenderHdrLayer(Layer layer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var output = _hdrLayers[layer];
            if (output.Rendered) return output;
            output.Rendered = true;
            output.Drawing.Clear(SKColors.Transparent);
            var definition = layer.Definition;
            var state = _frame.Layers.GetValueOrDefault(definition.Name);
            var available = false;
            try
            {
                if (definition.Media is not null)
                {
                    available = DrawHdrMedia(layer, output, state, token);
                }
                else
                {
                    WindowTarget? target = null;
                    if (!definition.Absolute || (DesktopCapture.UsesSourceSelection &&
                                                 definition.Source == CaptureSourceKind.Window))
                    {
                        var match = _preset with
                        {
                            Process = definition.Process ?? _preset.Process,
                            Title = definition.Process is null ? _preset.Title : definition.Title,
                        };
                        target = definition.FixedHandle != 0
                            ? _windows.SingleOrDefault(window => window.Handle == definition.FixedHandle)
                            : definition.Process is null && _preset.FixedHandle != 0
                                ? _windows.SingleOrDefault(window => window.Handle == _preset.FixedHandle)
                                : WindowLocator.Find(match, _windows, layer.Handle);
                        if (target is not null && _portalSource is null)
                            target = _context?.Refresh(target) ?? WindowLocator.Refresh(target);
                    }

                    if ((target?.Handle ?? 0) != layer.Handle)
                    {
                        layer.ReleaseCapture();
                        layer.Handle = target?.Handle ?? 0;
                        layer.FrozenArea = null;
                    }

                    if (target is not null || definition.Absolute)
                        available = DrawHdrCapture(layer, output, target, state, token);
                }
            }
            catch (COMException error) when (error.HResult == unchecked((int)0x80000013))
            {
                token.ThrowIfCancellationRequested();
                layer.ReleaseCapture();
            }
            catch (CaptureSourceLostException)
            {
                token.ThrowIfCancellationRequested();
                layer.ReleaseCapture();
            }

            if (!available) DrawHdrFallback(layer, output, token);
            if (state?.Effects is { Count: > 0 } effects)
            {
                output.Drawing.Flush();
                var placement = state.Motion?.At(_frameMotionTime) ?? state.Placement ?? definition.Placement;
                var mapped = MapEffects(effects, layer.InputWidth, layer.InputHeight, placement.Width,
                    placement.Height, output.Bitmap.Width, output.Bitmap.Height);
                var processed = HdrPixelEffectProcessor.Apply(ReadHdrBitmap(output.Bitmap,
                    new(output.IsHdrSource), 0), mapped, token);
                WriteLinearFrame(output.Bitmap, processed, token);
            }

            output.Drawing.Save();
            output.Drawing.ClipPath(layer.Mask, SKClipOperation.Difference, true);
            output.Drawing.Clear(SKColors.Transparent);
            output.Drawing.Restore();
            return output;
        }

        private bool DrawHdrCapture(Layer layer, HdrLayer output, WindowTarget? target, PresetLayerState? state,
            CancellationToken token)
        {
            var definition = layer.Definition;
            var basis = definition.Absolute ? default : definition.ClientArea ? target!.Client : target!.Bounds;
            PixelRect area;
            try
            {
                area = state?.Crop is { } crop
                    ? new(checked(basis.X + crop.X), checked(basis.Y + crop.Y), crop.Width, crop.Height)
                    : WindowPreset.ResolveArea(definition.Area, basis, _preset.Constants, _frame.Variables);
            }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            {
                return false;
            }

            if (!definition.Follow && state?.Crop is null)
            {
                layer.FrozenArea ??= area;
                area = layer.FrozenArea.Value;
            }

            layer.InputWidth = area.Width;
            layer.InputHeight = area.Height;
            CapturedFrameSet frames;
            SKRect? source = null;
            if (definition.Source == CaptureSourceKind.Screen)
            {
                if (!_topology.Covers(area)) return false;
                if (layer.Screen is null || area.Width != layer.LastArea.Width || area.Height != layer.LastArea.Height)
                {
                    layer.Screen?.Dispose();
                    layer.Screen = new(_options with { Region = area, Divisor = 1 },
                        _frameDisplayProvider, output.Bitmap.Width, output.Bitmap.Height, _portalSource?.Session,
                        _includedWindowTitle);
                }
                else if (area != layer.LastArea)
                {
                    layer.Screen.Move(area);
                }

                layer.LastArea = area;
                frames = layer.Screen.CaptureFrames(token);
            }
            else
            {
                PixelRect? actualBounds;
                if (_context is not null)
                {
                    var captured = _context.CaptureWindowFrames(target!.Handle, _options,
                        area, output.Bitmap.Width, output.Bitmap.Height, token, _portalSource?.Session);
                    if (captured.Frames is null) return false;
                    frames = captured.Frames;
                    actualBounds = captured.Bounds;
                }
                else
                {
                    layer.Window ??= new(target!.Handle, _options.Cursor, _options.Fps,
                        nativeSource: _portalSource?.Session);
                    layer.Window.ConfigureRegion(area, output.Bitmap.Width, output.Bitmap.Height);
                    frames = layer.Window.CaptureFrames(token);
                    actualBounds = layer.Window.CoordinateBounds;
                }

                var bounds = actualBounds ?? (OperatingSystem.IsLinux() ? target!.Client : target!.Bounds);
                if (area.Intersect(bounds).IsEmpty) return false;
                var sx = (float)frames.Sdr.Width / bounds.Width;
                var sy = (float)frames.Sdr.Height / bounds.Height;
                source = new SKRect((area.X - bounds.X) * sx, (area.Y - bounds.Y) * sy,
                    (area.Right - bounds.X) * sx, (area.Bottom - bounds.Y) * sy);
            }

            _hdrTimestamp = Math.Max(_hdrTimestamp, frames.TimestampTicks);
            if (frames.Hdr is null && frames.Sdr.ColorInfo.IsHdrSource)
                throw new NotSupportedException(UiText.Get("HdrSceneFrameUnavailable"));
            DrawHdrFrame(output, frames.Hdr ?? frames.Sdr, source, token);
            RecordInputArea(layer, area);
            return true;
        }

        private void DrawHdrFallback(Layer layer, HdrLayer output, CancellationToken token)
        {
            ReportWindowSourceLoss(layer, token);
            var fallback = layer.Definition.Missing;
            switch (fallback.Kind)
            {
                case "color":
                    output.Drawing.Clear(LinearColor(ParseColor(fallback.Value)));
                    break;
                case "image":
                    layer.FallbackImage ??= LoadImage(fallback.Value);
                    DrawHdrBitmap(output, layer.FallbackImage, null, token);
                    break;
                case "source":
                {
                    var other = _layers.Single(candidate => candidate.Definition.Name == fallback.Value);
                    var hdr = RenderHdrLayer(other, token);
                    using var paint = new SKPaint();
                    output.Drawing.DrawSampledBitmap(hdr.Bitmap,
                        new(0, 0, output.Bitmap.Width, output.Bitmap.Height), paint);
                    output.IsHdrSource = hdr.IsHdrSource;
                    break;
                }
            }
        }

        private static SKBitmap CreateHdrBitmap(int width, int height)
        {
            return new(new(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, HdrColorSpace));
        }

        private static SKColorF LinearColor(SKColor color)
        {
            return new(SrgbToLinear(color.Red / 255f),
                SrgbToLinear(color.Green / 255f), SrgbToLinear(color.Blue / 255f), color.Alpha / 255f);
        }

        private static float SrgbToLinear(float value)
        {
            return value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
        }

        private static CapturedFrame ReadHdrBitmap(SKBitmap bitmap, FrameColorInfo color, long revision)
        {
            var bytes = new byte[checked(bitmap.RowBytes * bitmap.Height)];
            Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
            return new(bitmap.Width, bitmap.Height, bitmap.RowBytes,
                CapturePixelFormat.Rgba16FloatScRgb, bytes, color, CaptureAlphaMode.Premultiplied, revision, []);
        }

        private void DisposeHdr()
        {
            foreach (var layer in _hdrLayers.Values) layer.Dispose();
            _hdrLayers.Clear();
            _hdrDrawing?.Dispose();
            _hdrCanvas?.Dispose();
        }
    }
}
