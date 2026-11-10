namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PinnedImageTranslationRaster? _activeTranslatedRaster;
        private ImageTextSelection _translatedTextSelection = new(ImageTextSelectionDocument.Empty);

        private ImageTextSelection ActiveTextSelection =>
            _activeTranslatedRaster is null ? _textSelection : _translatedTextSelection;
        private Task _rasterUpdateTail = Task.CompletedTask;

        private byte[] ProcessingSourcePng => _activeTranslatedRaster?.Content.Png ?? _png;
        private byte[] ProcessingSourcePixels => _activeTranslatedRaster?.Content.Pixels ?? _pixels;

        private CapturedFrame? ProcessingSourceHdr =>
            _activeTranslatedRaster is { } translated ? translated.Content.Hdr : _hdrSource;

        private byte[] ProcessingSourceSdrPng => _activeTranslatedRaster is { } translated
            ? translated.Content.SdrPng ?? translated.Content.Png
            : _sdrPng ?? _png;

        private async Task SerializeRasterUpdateAsync(Func<Task> update)
        {
            var previous = _rasterUpdateTail;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _rasterUpdateTail = completion.Task;
            try
            {
                await previous;
                await update();
            }
            finally
            {
                completion.SetResult();
            }
        }

        private async Task WaitForRasterUpdatesAsync(CancellationToken token)
        {
            while (true)
            {
                var pending = _rasterUpdateTail;
                await pending.WaitAsync(token);
                if (ReferenceEquals(pending, _rasterUpdateTail)) return;
            }
        }
    }
}
