using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow : IPinnedWindow
    {
        Window IPinnedWindow.Window => this;
        bool IPinnedWindow.CanArrangeSelection => CanArrangeSelection;
        bool IPinnedWindow.CanSaveSelection => CanSaveSelection;
        PixelRect IPinnedWindow.SelectionLayoutBounds => SelectionLayoutBounds;
        CancellationToken IPinnedWindow.OutputLifetime => OutputLifetime;

        Task<ScreenshotResult> IPinnedWindow.CapturePreferredOutputAsync(OutputColorMode mode,
            CancellationToken token)
        {
            return CapturePreferredOutputAsync(mode, token);
        }

        Task IPinnedWindow.CompleteSelectionSaveAsync(IReadOnlyList<string> paths, ScreenshotSaveProfile profile,
            ScreenshotResult output)
        {
            return CompleteSelectionSaveAsync(paths, profile, output);
        }
    }
}
