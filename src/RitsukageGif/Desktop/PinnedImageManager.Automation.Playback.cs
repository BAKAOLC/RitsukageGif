namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private sealed partial class AutomationService
        {
            public Task FocusAsync(string id, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    var pin = manager.AutomationFind(id);
                    if (!pin.Window.IsVisible)
                        throw new InvalidOperationException("Show the pin before activating it.");
                    pin.Window.Activate();
                    return Task.FromResult(true);
                }, token);
            }

            public Task<IReadOnlyList<string>> FilesAsync(string id, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    var pin = manager.AutomationFind(id);
                    IReadOnlyList<string> paths = pin is PinnedDocumentWindow { Document: PinnedFilesDocument files }
                        ? files.Paths.ToArray()
                        : [];
                    return Task.FromResult(paths);
                }, token);
            }

            public Task<ReadOnlyMemory<byte>> OriginalImageAsync(string id, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    var pin = manager.AutomationFind(id) as PinnedImageWindow ??
                              throw new NotSupportedException("The pin has no original image file.");
                    var original = pin.OriginalContent;
                    ReadOnlyMemory<byte> data = original.Encoded ?? original.Png;
                    return Task.FromResult(data);
                }, token);
            }

            public Task<PinPlaybackState> PlaybackAsync(string id, bool? paused = null, double? speed = null,
                CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    var pin = manager.AutomationFind(id) as PinnedImageWindow ??
                              throw new NotSupportedException("The pin is not an animation.");
                    return Task.FromResult(pin.SetAutomationPlayback(paused, speed));
                }, token);
            }

            public Task<PinPlaybackState> StepAsync(string id, int direction, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    var pin = manager.AutomationFind(id) as PinnedImageWindow ??
                              throw new NotSupportedException("The pin is not an animation.");
                    return pin.StepAutomationAsync(direction, token);
                }, token);
            }
        }
    }
}
