using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal interface IPinnedWindow
    {
        Window Window { get; }
        PinnedWindowPresentation Presentation { get; }
        string Id { get; }
        string Group { get; }
        bool IsVisible { get; }
        bool IsCloseLocked { get; }
        bool IsInteractionLocked { get; }
        bool IsIncomingDropLocked { get; }
        bool IsLocked { get; }
        bool IsMouseThrough { get; }
        bool CanSetMouseThrough { get; }
        bool CanArrangeSelection { get; }
        bool CanSaveSelection { get; }
        PixelRect SelectionLayoutBounds { get; }
        CancellationToken OutputLifetime { get; }
        void SetGroup(string value);
        void SetSelected(bool value);
        void SetLocked(bool value);
        void SetMouseThrough(bool value);
        void SetThumbnailDisplay(bool value, Avalonia.PixelPoint? screenAnchor = null);
        void RevealImage();
        void HideImage();
        void CloseImage(bool destroy);
        void DestroyForGroup();
        void RestoreInteraction();
        Task<ScreenshotResult> CaptureSceneOutputAsync(OutputColorMode mode, CancellationToken token);

        Task CompleteSelectionSaveAsync(IReadOnlyList<string> paths, ScreenshotSaveProfile profile,
            ScreenshotResult output);
    }
}
