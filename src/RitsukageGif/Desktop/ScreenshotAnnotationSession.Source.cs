using Avalonia;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private bool _sourceWasBusy;
        private CancellationTokenSource? _sourceWork;

        public async Task SetOverlayFrameAsync(PixelRect region, byte[]? pixels, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (pixels is not null && (region.IsEmpty || (long)region.Width * region.Height * 4 != pixels.Length))
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(pixels));
            // ReSharper disable once MethodHasAsyncOverload
            _sourceWork?.Cancel();
            if ((_overlayFrame is null && pixels is null) ||
                (_overlayFrame is { } old && old.Region == region && ReferenceEquals(old.Pixels, pixels))) return;
            if (_sourceWork is null) _sourceWasBusy = IsBusy;
            // ReSharper disable once MethodHasAsyncOverload
            _pixelPreviewWork?.Cancel();
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token, token);
            _sourceWork = work;
            IsBusy = true;
            Changed?.Invoke();
            var replacements =
                new List<(ScreenshotAnnotation Annotation, IReadOnlyList<(Bitmap Image, Rect Bounds)> Tiles)>();
            try
            {
                var frames = pixels is null ? _frames.ToArray() : _frames.Append((region, pixels)).ToArray();
                long bytes = 0;
                foreach (var (annotation, preceding) in SourcePreviewLayers())
                {
                    var tiles = await CreateSourcePreviewAsync(annotation, preceding, frames, work.Token);
                    replacements.Add((annotation, tiles));
                    bytes += tiles.Sum(tile => (long)tile.Image.PixelSize.Width * tile.Image.PixelSize.Height * 4);
                    if (bytes > 128L * 1024 * 1024)
                        throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
                }

                work.Token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_sourceWork, work)) throw new OperationCanceledException(work.Token);
                foreach (var (annotation, tiles) in replacements)
                {
                    var previous = annotation.ImageTiles;
                    annotation.ImageTiles = tiles;
                    foreach (var tile in previous) tile.Image.Dispose();
                }

                replacements.Clear();
                _overlayFrame = pixels is null ? null : (region, pixels);
            }
            finally
            {
                foreach (var (_, tiles) in replacements)
                foreach (var tile in tiles)
                    tile.Image.Dispose();
                if (ReferenceEquals(_sourceWork, work))
                {
                    _sourceWork = null;
                    IsBusy = _sourceWasBusy;
                    if (!_disposed) Changed?.Invoke();
                }
            }
        }

        public async Task ReplaceFramesAsync(IReadOnlyList<(PixelRect Region, byte[] Pixels)> frames,
            CancellationToken token, Action? commit = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (IsBusy || IsDrawing) throw new InvalidOperationException("Annotation interaction is active.");
            var snapshot = frames.ToArray();
            foreach (var frame in snapshot)
                if (frame.Region.IsEmpty || (long)frame.Region.Width * frame.Region.Height * 4 != frame.Pixels.Length)
                    throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(frames));
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token, token);
            _sourceWasBusy = IsBusy;
            // ReSharper disable once MethodHasAsyncOverload
            _pixelPreviewWork?.Cancel();
            _sourceWork = work;
            IsBusy = true;
            Changed?.Invoke();
            var replacements =
                new List<(ScreenshotAnnotation Annotation, IReadOnlyList<(Bitmap Image, Rect Bounds)> Tiles)>();
            try
            {
                var sampling = _overlayFrame is { } overlay ? [.. snapshot, overlay] : snapshot;
                long bytes = 0;
                foreach (var (annotation, preceding) in SourcePreviewLayers())
                {
                    var tiles = await CreateSourcePreviewAsync(annotation, preceding, sampling, work.Token);
                    replacements.Add((annotation, tiles));
                    bytes += tiles.Sum(tile => (long)tile.Image.PixelSize.Width * tile.Image.PixelSize.Height * 4);
                    if (bytes > 128L * 1024 * 1024)
                        throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
                }

                work.Token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                commit?.Invoke();
                _frames.Clear();
                _frames.AddRange(snapshot);
                foreach (var (annotation, tiles) in replacements)
                {
                    var previous = annotation.ImageTiles;
                    annotation.ImageTiles = tiles;
                    foreach (var tile in previous) tile.Image.Dispose();
                }

                replacements.Clear();
            }
            finally
            {
                foreach (var (_, tiles) in replacements)
                foreach (var tile in tiles)
                    tile.Image.Dispose();
                if (ReferenceEquals(_sourceWork, work))
                {
                    _sourceWork = null;
                    IsBusy = _sourceWasBusy;
                    if (!_disposed) Changed?.Invoke();
                }
            }
        }
    }
}
