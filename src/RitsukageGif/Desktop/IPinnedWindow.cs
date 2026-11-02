using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal interface IPinnedWindow
    {
        Window Window { get; }
        PinnedWindowPresentation Presentation { get; }
        string Id { get; }
        string Group { get; }
        string? Title { get; }
        bool IsVisible { get; }
        bool IsCloseLocked { get; }
        bool IsInteractionLocked { get; }
        bool IsIncomingDropLocked { get; }
        bool CanArrangeSelection { get; }
        bool CanSaveSelection { get; }
        PixelRect SelectionLayoutBounds { get; }
        CancellationToken OutputLifetime { get; }
        void SetGroup(string value);
        void SetSelected(bool value);
        void RevealImage();
        void HideImage();
        void CloseImage(bool destroy);
        void DestroyForGroup();
        void RestoreInteraction();
        Task<ScreenshotResult> CapturePreferredOutputAsync(OutputColorMode mode, CancellationToken token);

        Task CompleteSelectionSaveAsync(IReadOnlyList<string> paths, ScreenshotSaveProfile profile,
            ScreenshotResult output);
    }
}
