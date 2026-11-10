using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private void RefreshPendingWindowState(IPinnedWindow window)
        {
            switch (window)
            {
                case PinnedImageWindow image when _pendingRestore.TryGetValue(image, out var previousImage):
                {
                    var state = image.CaptureState();
                    PreservePendingVisibility(previousImage, state);
                    _pendingRestore[image] = state;
                    break;
                }
                case PinnedDocumentWindow document
                    when _pendingDocuments.TryGetValue(document, out var previousDocument):
                {
                    var state = document.CaptureDocumentState();
                    PreservePendingVisibility(previousDocument.Window, state.Window);
                    _pendingDocuments[document] = state;
                    break;
                }
            }
        }

        private static void PreservePendingVisibility(PinnedWindowState previous, PinnedWindowState current)
        {
            current.Visible = previous.Visible;
            current.VisibleInGroup = previous.VisibleInGroup;
            current.GroupHidden = previous.GroupHidden;
        }

        private async Task ReadVisibilityMaskAsync(CancellationToken token)
        {
            var path = Path.Combine(_sessionDirectory!, "pin-visibility.json");
            if (!File.Exists(path)) return;
            try
            {
                await using var stream = File.OpenRead(path);
                _visibilityMask.Initialize(await JsonSerializer.DeserializeAsync(stream,
                    PinnedImageJsonContext.Default.Boolean, token));
            }
            catch (Exception error) when (error is IOException or JsonException)
            {
                PinLogger.ZLogWarning(error, $"Pin visibility state could not be read.");
            }
        }

        private async Task WriteVisibilityMaskAsync(string directory, CancellationToken token)
        {
            var path = Path.Combine(directory, "pin-visibility.json");
            var staging = path + ".tmp";
            try
            {
                await using (var stream = File.Create(staging))
                    await JsonSerializer.SerializeAsync(stream, AllPinsHidden,
                        PinnedImageJsonContext.Default.Boolean, token);
                File.Move(staging, path, true);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }
    }
}
