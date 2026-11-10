using Avalonia;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        internal Func<ScreenshotResult, PixelPoint, CancellationToken, Task>? CutOutRequested { get; set; }

        private async Task CutOutThumbnailAsync()
        {
            if (!_isThumbnail || !CanUseInlineTranslation || CutOutRequested is not { } create) return;
            var token = _lifetime.Token;
            var snapshot = SnapshotImageViewport() with { Opacity = 1 };
            var origin = _viewport.PointToScreen(default);
            var output = await PinnedViewportOutput.ProjectAsync(
                CaptureSceneOutputAsync(OutputColorMode.Both, token), snapshot, token);
            await create(output with { WindowShape = null }, new(origin.X, origin.Y), token);
            RequestClose(true);
        }
    }
}
