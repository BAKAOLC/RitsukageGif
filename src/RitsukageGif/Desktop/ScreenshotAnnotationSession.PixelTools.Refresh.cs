using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Point = Avalonia.Point;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private PixelRect _pixelPreviewBounds;
        private (PixelRect Region, byte[] Pixels)[] _pixelPreviewFrames = [];
        private bool _pixelPreviewQueued;
        private (ScreenshotAnnotation Original, Point[] Points)[] _pixelPreviewState = [];
        private CancellationTokenSource? _pixelPreviewWork;

        private bool PixelPreviewMatches()
        {
            if (_pixelPreviewBounds != AnnotationBounds || _pixelPreviewState.Length != _annotations.Count ||
                _pixelPreviewFrames.Length != _frames.Count + (_overlayFrame is null ? 0 : 1)) return false;
            for (var index = 0; index < _annotations.Count; index++)
                if (!ReferenceEquals(_annotations[index], _pixelPreviewState[index].Original) ||
                    !_annotations[index].Points.SequenceEqual(_pixelPreviewState[index].Points))
                    return false;
            for (var index = 0; index < _frames.Count; index++)
                if (_frames[index].Region != _pixelPreviewFrames[index].Region ||
                    !ReferenceEquals(_frames[index].Pixels, _pixelPreviewFrames[index].Pixels))
                    return false;
            return _overlayFrame is not { } overlay || (overlay.Region == _pixelPreviewFrames[^1].Region &&
                                                        ReferenceEquals(overlay.Pixels,
                                                            _pixelPreviewFrames[^1].Pixels));
        }

        private void RefreshPixelPreviews()
        {
            if (_pixelPreviewQueued || !CanRefreshPixelPreviews()) return;
            _pixelPreviewQueued = true;
            Dispatcher.UIThread.Post(RefreshQueuedPixelPreviews);
        }

        private bool CanRefreshPixelPreviews()
        {
            return !_disposed && !IsBusy && !IsDrawing && !PixelPreviewMatches() &&
                   _annotations.Any(annotation =>
                       annotation.Tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase);
        }

        private void RefreshQueuedPixelPreviews()
        {
            _pixelPreviewQueued = false;
            if (!CanRefreshPixelPreviews()) return;
            _pixelPreviewWork?.Cancel();
            _pixelPreviewState = [.. _annotations.Select(annotation => (annotation, annotation.Points.ToArray()))];
            _pixelPreviewBounds = AnnotationBounds;
            _pixelPreviewFrames = PixelPreviewFrames();
            var snapshot = _annotations.Select(annotation => CloneAnnotation(annotation)).ToArray();
            var targets = _annotations.ToArray();
            _ = RefreshPixelPreviewsAsync(snapshot, targets);
        }

        private async Task RefreshPixelPreviewsAsync(ScreenshotAnnotation[] snapshot, ScreenshotAnnotation[] targets)
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _pixelPreviewWork = work;
            List<(ScreenshotAnnotation Target, IReadOnlyList<(Bitmap Image, Rect Bounds)> Tiles)> replacements = [];
            try
            {
                for (var index = 0; index < snapshot.Length; index++)
                {
                    var annotation = snapshot[index];
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (annotation.Tool)
                    {
                        case ScreenshotTool.Magnifier:
                        case ScreenshotTool.Eraser:
                            annotation.ImageTiles = await CreateImageEffectAsync(annotation, work.Token, index);
                            break;
                        case ScreenshotTool.Mosaic:
                        case ScreenshotTool.Blur:
                        case ScreenshotTool.SmartErase:
                            replacements.Add((targets[index], await CreatePixelPreviewAsync(annotation,
                                snapshot[..index], work.Token)));
                            break;
                    }
                }

                work.Token.ThrowIfCancellationRequested();
                if (_disposed || !ReferenceEquals(_pixelPreviewWork, work) || !PixelPreviewMatches()) return;
                foreach (var (target, tiles) in replacements)
                {
                    var previous = target.ImageTiles;
                    target.ImageTiles = tiles;
                    foreach (var tile in previous) tile.Image.Dispose();
                }

                replacements.Clear();
                Changed?.Invoke();
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_disposed)
                {
                    Error = error.Message;
                    Logger.ZLogWarning(error, $"Pixel annotation preview failed. SessionId={_logSessionId}");
                    Changed?.Invoke();
                }
            }
            finally
            {
                foreach (var annotation in snapshot) annotation.Dispose();
                foreach (var (_, tiles) in replacements)
                foreach (var tile in tiles)
                    tile.Image.Dispose();
                if (ReferenceEquals(_pixelPreviewWork, work)) _pixelPreviewWork = null;
            }
        }
    }
}
