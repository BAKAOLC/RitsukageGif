using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture
    {
        private static void DrawHdrPixels(HdrLayer output, ReadOnlyMemory<byte> pixels, int width, int height,
            SKRect crop, CancellationToken token)
        {
            DrawHdrFrame(output,
                new(width, height, checked(width * 4), CapturePixelFormat.Bgra8Srgb, pixels,
                    new(false), CaptureAlphaMode.Straight, 0, []), crop, token);
        }

        private bool DrawHdrMedia(Layer layer, HdrLayer output, PresetLayerState? state, CancellationToken token)
        {
            if (Environment.TickCount64 < layer.NextMediaRetry) return false;
            var definition = layer.Definition;
            var media = definition.Media!;
            try
            {
                if (media.Channel)
                {
                    if (state?.Playback?.Paused != true || layer.InputFrame is null)
                        layer.InputFrame = _context?.ReadInput(media, state?.Playback?.HoldLast);
                    var input = layer.InputFrame;
                    if (input is null) return false;
                    var crop = state?.Crop ?? WindowPreset.ResolveArea(definition.Area,
                        new(0, 0, input.Width, input.Height), _preset.Constants, _frame.Variables);
                    layer.InputWidth = crop.Width;
                    layer.InputHeight = crop.Height;
                    DrawHdrPixels(output, input.Pixels, input.Width, input.Height,
                        new(crop.X, crop.Y, crop.Right, crop.Bottom), token);
                    return true;
                }

                if (layer.MediaPath is null)
                {
                    var directory = _preset.SourceFile is null
                        ? Environment.CurrentDirectory
                        : Path.GetDirectoryName(Path.GetFullPath(_preset.SourceFile))!;
                    layer.MediaPath = media.Ffmpeg && Uri.TryCreate(media.File, UriKind.Absolute, out var uri) &&
                                      !uri.IsFile
                        ? media.File
                        : Path.GetFullPath(Environment.ExpandEnvironmentVariables(media.File), directory);
                }

                var path = layer.MediaPath;
                if (media.Ffmpeg)
                {
                    layer.ResolvedMedia ??= media with { File = path };
                    layer.Ffmpeg ??= new();
                    var frame = layer.Ffmpeg.Read(layer.ResolvedMedia, state?.Playback, _frameMotionTime,
                        _context?.ElapsedSeconds ?? _clock.Elapsed.TotalSeconds, token);
                    if (frame.Pixels is null) return false;
                    var crop = state?.Crop ?? WindowPreset.ResolveArea(definition.Area,
                        new(0, 0, frame.Width, frame.Height), _preset.Constants, _frame.Variables);
                    layer.InputWidth = crop.Width;
                    layer.InputHeight = crop.Height;
                    DrawHdrPixels(output, frame.Pixels, frame.Width, frame.Height,
                        new(crop.X, crop.Y, crop.Right, crop.Bottom), token);
                    return true;
                }

                var source = _context?.GetMedia(path, token) ?? (layer.Media ??= new(path, token: token));
                var area = state?.Crop ?? WindowPreset.ResolveArea(definition.Area,
                    new(0, 0, source.Width, source.Height), _preset.Constants, _frame.Variables);
                layer.InputWidth = area.Width;
                layer.InputHeight = area.Height;
                var playback = state?.Playback;
                var elapsed = playback is null ? _context?.ElapsedSeconds ?? _clock.Elapsed.TotalSeconds :
                    playback.Paused ? 0 : Math.Max(0, _frameMotionTime - playback.UpdatedAt);
                var bitmap = source.FrameAt(elapsed, playback?.Loop ?? media.Loop,
                    playback?.Speed ?? media.Speed, playback?.Position ?? media.Position,
                    !(playback?.HoldLast ?? media.HoldLast));
                if (bitmap is null) return false;
                DrawHdrBitmap(output, bitmap, new SKRect(area.X, area.Y, area.Right, area.Bottom), token);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or OverflowException)
            {
                token.ThrowIfCancellationRequested();
                layer.NextMediaRetry = Environment.TickCount64 + 1000;
                return false;
            }
        }
    }
}
