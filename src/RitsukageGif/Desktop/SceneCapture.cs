using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture : IFrameSource
    {
        private readonly WindowPreset _preset;
        private readonly PresetRuntime? _runtime;
        private readonly SceneCaptureContext? _context;
        private readonly bool _ownsContext;
        private readonly PresetRuntimeSnapshot _staticFrame;
        private readonly bool _needsWindowTargets;

        private PresetRuntimeSnapshot _frame;
        private double _frameMotionTime;

        private readonly RecordingOptions _options;
        private readonly Func<DisplayTopology> _displayProvider;
        private readonly Func<DisplayTopology> _frameDisplayProvider;
        private DisplayTopology _topology;
        private bool _disposed;
        private readonly SKBitmap _canvas;
        private readonly SKCanvas _drawing;
        private readonly SKPaint _paint = new();
        private readonly SKColor _background;
        private readonly Layer[] _layers;
        private readonly Stopwatch _clock = new();
        private double _nextSearch;
        private IReadOnlyList<WindowTarget> _windows = [];

        public SceneCapture(RecordingOptions options, PixelRect[] monitors, WindowPreset preset,
            PresetRuntime? runtime = null, SceneCaptureContext? context = null, ExternalInputHub? inputs = null)
            : this(options, ConstantDisplay(monitors), preset, runtime, context, inputs)
        {
        }

        private static Func<DisplayTopology> ConstantDisplay(PixelRect[] monitors)
        {
            var topology = new DisplayTopology(monitors);
            return () => topology;
        }

        public SceneCapture(RecordingOptions options, Func<DisplayTopology> displayProvider, WindowPreset preset,
            PresetRuntime? runtime = null, SceneCaptureContext? context = null, ExternalInputHub? inputs = null)
        {
            _runtime = runtime;
            _ownsContext = context is null;
            _context = context ?? new SceneCaptureContext(options.Fps, inputs: inputs);
            _staticFrame = new PresetRuntimeSnapshot(preset.InitialValues ?? new Dictionary<string, object>(),
                new Dictionary<string, PresetLayerState>());
            _frame = _staticFrame;
            _preset = preset;
            _options = options;
            _displayProvider = displayProvider;
            _topology = displayProvider();
            _frameDisplayProvider = () => _topology;
            _canvas = new SKBitmap(options.Width, options.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            _drawing = new SKCanvas(_canvas);
            _paint.FilterQuality = SKFilterQuality.Medium;
            _paint.IsAntialias = true;
            _background = preset.Layout is null ? SKColors.Transparent : ParseColor(preset.Layout.Background);
            var definitions = preset.Layout?.Layers ??
            [
                new PresetLayer(preset.Name, null, preset.Title, preset.ClientArea,
                    preset.Area, new PixelRect(0, 0, options.Region.Width, options.Region.Height),
                    new PresetShape("rect"),
                    preset.Source, preset.Missing ?? new PresetFallback(), preset.Follow, preset.Absolute)
            ];
            _needsWindowTargets = definitions.Any(definition => !definition.Absolute && definition.Media is null);
            var layers = new List<Layer>();
            try
            {
                if (DesktopCapture.UsesSourceSelection &&
                    (definitions.Any(definition => !definition.Absolute && definition.Media is null) ||
                     definitions.Where(definition => definition.Media is null).Select(definition => definition.Source)
                         .Distinct().Count() > 1))
                    throw new NotSupportedException(UiText.Get("CapturePlatformUnsupported"));
                foreach (var definition in definitions) layers.Add(new Layer(definition, options.Divisor));
                _layers = layers.ToArray();
            }
            catch
            {
                foreach (var layer in layers) layer.Dispose();
                _drawing.Dispose();
                _canvas.Dispose();
                _paint.Dispose();
                throw;
            }
        }

        public byte[] Capture() => Capture(default);

        public byte[] Capture(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ownsContext) _context!.BeginFrame(_clock.Elapsed.TotalSeconds);
            _topology = _context?.GetTopology(_displayProvider) ?? _displayProvider();
            token.ThrowIfCancellationRequested();
            _frame = _runtime is null ? _staticFrame : _context?.GetSnapshot(_runtime) ?? _runtime.Snapshot;
            _frameMotionTime = _context?.MotionTime ?? PresetMotion.Now;
            if (_needsWindowTargets && _context is not null) _windows = _context.GetWindows();
            else if (_needsWindowTargets && _clock.Elapsed.TotalMilliseconds >= _nextSearch)
            {
                _windows = WindowLocator.List();
                _nextSearch = _clock.Elapsed.TotalMilliseconds + 100;
            }

            foreach (var layer in _layers)
            {
                token.ThrowIfCancellationRequested();
                layer.Rendered = false;
                var state = _frame.Layers.GetValueOrDefault(layer.Definition.Name);
                var placement = state?.Placement ?? layer.Definition.Placement;
                var width = Math.Max(placement.Width, state?.Motion?.From.Width ?? 0);
                var height = Math.Max(placement.Height, state?.Motion?.From.Height ?? 0);
                layer.EnsureResolution(width, height, _options.Divisor);
            }

            _drawing.Clear(_background);
            foreach (var layer in _layers)
            {
                token.ThrowIfCancellationRequested();
                var state = _frame.Layers.GetValueOrDefault(layer.Definition.Name);
                if (state?.Visible == false) continue;
                _paint.Color = SKColors.White;
                Render(layer, token);
                var p = state?.Motion?.At(_frameMotionTime) ??
                        state?.Placement ?? layer.Definition.Placement;
                _paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * (state?.Opacity ?? 1)));
                var scale = (float)_options.Divisor;
                _drawing.DrawBitmap(layer.Bitmap,
                    new SKRect(p.X / scale, p.Y / scale, (p.X + p.Width) / scale, (p.Y + p.Height) / scale), _paint);
            }

            _paint.Color = SKColors.White;
            var pixels = new byte[checked(_options.Width * _options.Height * 4)];
            using var pixmap = _canvas.PeekPixels();
            unsafe
            {
                fixed (byte* address = pixels)
                    if (!pixmap.ReadPixels(_canvas.Info.WithAlphaType(SKAlphaType.Unpremul), (nint)address,
                            _options.Width * 4, 0, 0))
                        throw new InvalidOperationException(UiText.Get("InvalidFrameBuffer"));
            }

            token.ThrowIfCancellationRequested();
            if (_ownsContext && !_clock.IsRunning) _clock.Start();
            return pixels;
        }

        private void Render(Layer layer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (layer.Rendered) return;
            layer.Rendered = true;
            var definition = layer.Definition;
            WindowTarget? target = null;
            if (!definition.Absolute && definition.Media is null)
            {
                var match = _preset with
                {
                    Process = definition.Process ?? _preset.Process,
                    Title = definition.Process is null ? _preset.Title : definition.Title
                };
                target = definition.Process is null && _preset.FixedHandle != 0
                    ? _windows.SingleOrDefault(w => w.Handle == _preset.FixedHandle)
                    : WindowLocator.Find(match, _windows, layer.Handle);
                if (target is not null)
                    target = _context is null ? WindowLocator.Refresh(target) : _context.Refresh(target);
            }

            if ((target?.Handle ?? 0) != layer.Handle)
            {
                layer.ReleaseCapture();
                layer.Handle = target?.Handle ?? 0;
                layer.FrozenArea = null;
            }

            var state = _frame.Layers.GetValueOrDefault(definition.Name);
            var effects = state?.Effects;
            var sourceWidth = state?.Crop?.Width ?? layer.InputWidth;
            var sourceHeight = state?.Crop?.Height ?? layer.InputHeight;
            var drawing = layer.Drawing;
            drawing.Clear(SKColors.Transparent);
            drawing.Save();
            try
            {
                if (effects is not { Count: > 0 }) drawing.ClipPath(layer.Mask, SKClipOperation.Intersect, true);
                var available = false;
                if (definition.Media is not null)
                {
                    available = DrawMedia(layer, state, token, out sourceWidth, out sourceHeight);
                }
                else if (target is not null || definition.Absolute)
                {
                    PixelRect area;
                    try
                    {
                        area = WindowPreset.ResolveArea(definition.Area,
                            definition.Absolute ? default : definition.ClientArea ? target!.Client : target!.Bounds,
                            _preset.Constants, _frame.Variables);
                    }
                    catch (Exception ex) when (ex is ArgumentException or OverflowException)
                    {
                        DrawFallback(layer, token);
                        return;
                    }

                    if (_frame.Layers.GetValueOrDefault(definition.Name)?.Crop is { } crop)
                    {
                        var basis = definition.Absolute ? default :
                            definition.ClientArea ? target!.Client : target!.Bounds;
                        area = new PixelRect(checked(basis.X + crop.X), checked(basis.Y + crop.Y), crop.Width,
                            crop.Height);
                    }

                    if (!definition.Follow && _frame.Layers.GetValueOrDefault(definition.Name)?.Crop is null)
                    {
                        layer.FrozenArea ??= area;
                        area = layer.FrozenArea.Value;
                    }

                    sourceWidth = layer.InputWidth = area.Width;
                    sourceHeight = layer.InputHeight = area.Height;
                    if (definition.Source == CaptureSourceKind.Screen || DesktopCapture.UsesSourceSelection)
                    {
                        if (!_topology.Covers(area))
                        {
                            layer.Screen?.Dispose();
                            layer.Screen = null;
                            DrawFallback(layer, token);
                            return;
                        }

                        byte[] CaptureScreen()
                        {
                            if (layer.Screen is null || area.Width != layer.LastArea.Width ||
                                area.Height != layer.LastArea.Height)
                            {
                                layer.Screen?.Dispose();
                                layer.Screen = new DesktopCapture(_options with { Region = area, Divisor = 1 },
                                    _frameDisplayProvider, layer.Bitmap.Width, layer.Bitmap.Height);
                            }
                            else if (area != layer.LastArea) layer.Screen.Move(area);

                            layer.LastArea = area;
                            return layer.Screen.Capture(token);
                        }

                        var pixels = _context is null
                            ? CaptureScreen()
                            : _context.CaptureScreen(area, layer.Bitmap.Width, layer.Bitmap.Height, _options,
                                CaptureScreen);
                        if (pixels is not null)
                        {
                            DrawPixels(layer, pixels, layer.Bitmap.Width, layer.Bitmap.Height, null);
                            available = true;
                        }
                    }
                    else
                    {
                        byte[]? pixels;
                        int width, height;
                        if (_context is not null)
                            (pixels, width, height) = _context.CaptureWindow(target!.Handle, _options, token);
                        else
                        {
                            layer.Window ??= new WindowContentCapture(target!.Handle, _options.Cursor, _options.Fps);
                            pixels = layer.Window.Capture(token);
                            width = layer.Window.Width;
                            height = layer.Window.Height;
                        }

                        if (pixels is not null)
                        {
                            var captureBounds = OperatingSystem.IsLinux() ? target!.Client : target!.Bounds;
                            var scaleX = (float)width / captureBounds.Width;
                            var scaleY = (float)height / captureBounds.Height;
                            var local = new SKRect((area.X - captureBounds.X) * scaleX,
                                (area.Y - captureBounds.Y) * scaleY,
                                (area.Right - captureBounds.X) * scaleX, (area.Bottom - captureBounds.Y) * scaleY);
                            DrawPixels(layer, pixels, width, height, local);
                            available = true;
                        }
                    }
                }

                if (!available) DrawFallback(layer, token);
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80000013))
            {
                token.ThrowIfCancellationRequested();
                layer.ReleaseCapture();
                DrawFallback(layer, token);
            }
            catch (CaptureSourceLostException)
            {
                token.ThrowIfCancellationRequested();
                if (layer.Screen is null) layer.ReleaseCapture();
                DrawFallback(layer, token);
            }
            finally
            {
                drawing.Restore();
                if (effects is { Count: > 0 })
                {
                    var placement = state?.Motion?.At(_frameMotionTime) ?? state?.Placement ?? definition.Placement;
                    ApplyEffects(layer, effects, sourceWidth, sourceHeight, placement, token);
                }
            }
        }

        private unsafe void DrawPixels(Layer layer, ReadOnlySpan<byte> pixels, int width, int height, SKRect? source)
        {
            if (layer.SourceBitmap is null || layer.SourceBitmap.Width != width || layer.SourceBitmap.Height != height)
            {
                layer.SourceBitmap?.Dispose();
                layer.SourceBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            }

            pixels.CopyTo(new Span<byte>((void*)layer.SourceBitmap.GetPixels(), pixels.Length));
            layer.SourceBitmap.NotifyPixelsChanged();
            layer.Drawing.DrawBitmap(layer.SourceBitmap, source ?? new SKRect(0, 0, width, height),
                new SKRect(0, 0, layer.Bitmap.Width, layer.Bitmap.Height), _paint);
        }

        private void DrawFallback(Layer layer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var fallback = layer.Definition.Missing;
            var rect = new SKRect(0, 0, layer.Bitmap.Width, layer.Bitmap.Height);
            switch (fallback.Kind)
            {
                case "color":
                    using (var color = new SKPaint())
                    {
                        color.Color = ParseColor(fallback.Value);
                        layer.Drawing.DrawRect(rect, color);
                    }

                    break;
                case "image":
                    layer.FallbackImage ??= LoadImage(fallback.Value);
                    layer.Drawing.DrawBitmap(layer.FallbackImage, rect, _paint);
                    break;
                case "source":
                    var other = _layers.Single(l => l.Definition.Name == fallback.Value);
                    Render(other, token);
                    layer.Drawing.DrawBitmap(other.Bitmap, rect, _paint);
                    break;
            }
        }

        private static SKBitmap LoadImage(string path)
        {
            using var codec = SKCodec.Create(Environment.ExpandEnvironmentVariables(path)) ??
                              throw new InvalidDataException(UiText.Get("DslFallbackImage"));
            if ((long)codec.Info.Width * codec.Info.Height > 64 * 1024 * 1024)
                throw new InvalidDataException(UiText.Get("RecordingTooLarge"));
            var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException(UiText.Get("DslFallbackImage"));
            return bitmap;
        }

        private static SKColor ParseColor(string value) =>
            value == "transparent" ? SKColors.Transparent : SKColor.Parse(value);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var layer in _layers) layer.Dispose();
            if (_ownsContext) _context!.Dispose();
            _drawing.Dispose();
            _canvas.Dispose();
            _paint.Dispose();
        }

        private sealed class Layer : IDisposable
        {
            public readonly PresetLayer Definition;
            public SKBitmap Bitmap;
            public SKCanvas Drawing;
            public readonly SKPath Mask = new();
            public SKBitmap? SourceBitmap, FallbackImage;
            public DesktopCapture? Screen;
            public WindowContentCapture? Window;
            public nint Handle;
            public PixelRect LastArea;
            public PixelRect? FrozenArea;
            public bool Rendered;
            public int InputWidth, InputHeight;
            public MediaFrameSource? Media;
            public string? MediaPath;
            public PresetMediaSource? ResolvedMedia;
            public SceneFfmpegSource? Ffmpeg;
            public ExternalInputFrame? InputFrame;
            public long NextMediaRetry;
            public IReadOnlyList<PixelEffect>? Effects;
            public PixelEffect[] MappedEffects = [];

            public (int SourceWidth, int SourceHeight, int OutputWidth, int OutputHeight, int Width, int Height)
                EffectSize;

            public Layer(PresetLayer definition, int divisor)
            {
                Definition = definition;
                var p = definition.Placement;
                InputWidth = p.Width;
                InputHeight = p.Height;
                Bitmap = new SKBitmap(Math.Max(1, p.Width / divisor), Math.Max(1, p.Height / divisor),
                    SKColorType.Bgra8888,
                    SKAlphaType.Premul);
                Drawing = new SKCanvas(Bitmap);
                BuildMask();
            }

            public void EnsureResolution(int width, int height, int divisor)
            {
                width = Math.Max(Bitmap.Width, (width + divisor - 1) / divisor);
                height = Math.Max(Bitmap.Height, (height + divisor - 1) / divisor);
                if (width == Bitmap.Width && height == Bitmap.Height) return;
                var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                Drawing.Dispose();
                Bitmap.Dispose();
                Bitmap = bitmap;
                Drawing = new SKCanvas(Bitmap);
                Screen?.Dispose();
                Screen = null;
                BuildMask();
            }

            private void BuildMask()
            {
                Mask.Reset();
                var definition = Definition;
                var scaleX = (float)Bitmap.Width / definition.Placement.Width;
                var scaleY = (float)Bitmap.Height / definition.Placement.Height;
                var rect = new SKRect(0, 0, Bitmap.Width, Bitmap.Height);
                switch (definition.Shape.Kind)
                {
                    case "ellipse": Mask.AddOval(rect); break;
                    case "rounded":
                        Mask.AddRoundRect(rect, (float)definition.Shape.Radius * scaleX,
                            (float)definition.Shape.Radius * scaleY); break;
                    case "polygon":
                        var points = definition.Shape.Points!;
                        Mask.MoveTo((float)points[0] * scaleX, (float)points[1] * scaleY);
                        for (var i = 2; i < points.Count; i += 2)
                            Mask.LineTo((float)points[i] * scaleX, (float)points[i + 1] * scaleY);
                        Mask.Close();
                        break;
                    default: Mask.AddRect(rect); break;
                }
            }

            public void ReleaseCapture()
            {
                Screen?.Dispose();
                Screen = null;
                Window?.Dispose();
                Window = null;
            }

            public void Dispose()
            {
                ReleaseCapture();
                Media?.Dispose();
                Ffmpeg?.Dispose();
                InputFrame = null;
                SourceBitmap?.Dispose();
                FallbackImage?.Dispose();
                Drawing.Dispose();
                Bitmap.Dispose();
                Mask.Dispose();
            }
        }
    }
}
