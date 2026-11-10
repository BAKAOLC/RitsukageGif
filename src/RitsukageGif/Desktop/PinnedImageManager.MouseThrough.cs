using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        internal bool CanToggleMouseThrough => AllWindows.Any(window => window.CanSetMouseThrough);

        internal void ToggleMouseThroughAtPointer(Avalonia.PixelPoint point)
        {
            Dispatcher.UIThread.VerifyAccess();
            var target = AllWindows.Where(window => window.SelectionLayoutBounds.Contains(new(point.X, point.Y)))
                .MinBy(window => window.Presentation.RegistrationOrder);
            if (target is not null)
            {
                if (!target.CanSetMouseThrough)
                    throw new NotSupportedException(UiText.Get("PinnedMouseThroughUnavailable"));
                target.SetMouseThrough(!target.IsMouseThrough);
                return;
            }

            foreach (var window in AllWindows.Where(window => window.IsMouseThrough)) window.SetMouseThrough(false);
        }
    }
}
