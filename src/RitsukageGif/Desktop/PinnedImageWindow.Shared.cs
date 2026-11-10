using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow : IPinnedWindow
    {
        Window IPinnedWindow.Window => this;
        bool IPinnedWindow.IsMouseThrough => IsMouseThrough;
        bool IPinnedWindow.CanSetMouseThrough => PinnedWindowPlatform.CanSetMouseThrough(this);
        bool IPinnedWindow.CanArrangeSelection => CanArrangeSelection;
        bool IPinnedWindow.CanSaveSelection => CanSaveSelection;
        PixelRect IPinnedWindow.SelectionLayoutBounds => SelectionLayoutBounds;
        CancellationToken IPinnedWindow.OutputLifetime => OutputLifetime;

        void IPinnedWindow.SetMouseThrough(bool value)
        {
            SetMouseThrough(value);
        }


        Task<ScreenshotResult> IPinnedWindow.CaptureSceneOutputAsync(OutputColorMode mode, CancellationToken token)
        {
            return CaptureSceneOutputAsync(mode, token);
        }

        Task IPinnedWindow.CompleteSelectionSaveAsync(IReadOnlyList<string> paths, ScreenshotSaveProfile profile,
            ScreenshotResult output)
        {
            return CompleteSelectionSaveAsync(paths, profile, output);
        }
    }
}
