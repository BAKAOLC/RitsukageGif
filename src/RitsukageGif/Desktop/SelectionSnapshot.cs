namespace RitsukageGif.Desktop
{
    internal sealed record SelectionSnapshot(
        SelectionPhase Phase,
        PixelRect Region,
        WindowTarget? Target,
        bool WindowSource,
        bool Controls,
        bool FollowsWindow,
        bool CanConfirm,
        bool Cursor = false);
}
