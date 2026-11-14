namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private const int DuplicatePreviewPixelLimit = 256 * 1024;
        private Task? _duplicatePixelCommit;
        private CancellationTokenSource? _duplicatePreviewCancellation;
        private bool _duplicatePreviewPending;
        private Task? _duplicatePreviewTask;

        private static bool IsDuplicablePixelAnnotation(ScreenshotAnnotation annotation)
        {
            return annotation is
            {
                Tool: ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.Eraser,
                AutoMosaicId: var id,
                Properties.PixelPath: AnnotationPixelPath.Rectangle,
            } && id == Guid.Empty;
        }

        private void QueueDuplicatePixelPreview()
        {
            _duplicatePreviewPending = true;
            if (_duplicatePreviewTask is { IsCompleted: false }) return;
            var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _duplicatePreviewCancellation = work;
            _duplicatePreviewTask = UpdateDuplicatePixelPreviewAsync(work);
        }

        private void CancelDuplicatePixelPreview()
        {
            _duplicatePreviewPending = false;
            _duplicatePreviewCancellation?.Cancel();
        }

        private static void ClearDuplicatePixelTiles(ScreenshotAnnotation draft)
        {
            foreach (var tile in draft.ImageTiles) tile.Image.Dispose();
            draft.ImageTiles = [];
        }

        private async Task CancelDuplicatePixelWorkForSourceAsync()
        {
            if (_duplicateSource is not null && _gesture is { } draft && IsDuplicablePixelAnnotation(draft))
                CancelGesture();
            if (_duplicatePixelCommit is { IsCompleted: false } commit)
            {
                if (_gestureWork is { } work) await work.CancelAsync();
                await commit;
            }
            else if (_duplicatePreviewTask is { IsCompleted: false } preview)
            {
                await preview;
            }
        }

        private async Task UpdateDuplicatePixelPreviewAsync(CancellationTokenSource work)
        {
            try
            {
                while (_duplicatePreviewPending && !_disposed && !work.IsCancellationRequested &&
                       _duplicateSource is not null && _gesture is { } draft && IsDuplicablePixelAnnotation(draft))
                {
                    _duplicatePreviewPending = false;
                    var bounds = AnnotationBounds;
                    var area = PixelArea(draft, bounds);
                    if (area.IsEmpty) continue;
                    var sampling = PixelSamplingArea(draft, area, bounds);
                    if (draft.Tool != ScreenshotTool.Eraser)
                        sampling = ExpandPixelPreviewRegion(sampling, _annotations);
                    if ((long)sampling.Width * sampling.Height > DuplicatePreviewPixelLimit) break;
                    using var snapshot = CloneAnnotation(draft);
                    var tiles = await CreateImageEffectAsync(snapshot, work.Token);
                    try
                    {
                        if (work.IsCancellationRequested || _disposed || !ReferenceEquals(draft, _gesture) ||
                            bounds != AnnotationBounds || !draft.Points.SequenceEqual(snapshot.Points)) continue;
                        ClearDuplicatePixelTiles(draft);
                        draft.ImageTiles = tiles;
                        tiles = [];
                        Changed?.Invoke();
                    }
                    finally
                    {
                        foreach (var tile in tiles) tile.Image.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_disposed && !work.IsCancellationRequested)
                    Logger.ZLogDebug(error, $"Annotation duplicate preview unavailable. SessionId={_logSessionId}");
            }
            finally
            {
                if (ReferenceEquals(_duplicatePreviewCancellation, work)) _duplicatePreviewCancellation = null;
                work.Dispose();
            }

            if (_duplicatePreviewPending && !_disposed)
            {
                _duplicatePreviewTask = null;
                QueueDuplicatePixelPreview();
            }
        }
    }
}
