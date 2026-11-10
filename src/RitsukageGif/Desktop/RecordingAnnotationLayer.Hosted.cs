using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingAnnotationLayer
    {
        private readonly HashSet<Window> _hostedOwners = [];

        internal ScreenshotAnnotationSurface CreateHostedSurface(Window owner, PixelRect region)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_hostedOwners.Add(owner))
            {
                owner.AddHandler(InputElement.KeyDownEvent, HandleOwnedKeyDown, RoutingStrategies.Tunnel);
                owner.AddHandler(InputElement.KeyUpEvent, HandleOwnedKeyUp, RoutingStrategies.Tunnel);
                owner.Deactivated += KeyboardOwnerDeactivated;
            }

            _overlay.Update(owner, region, false, true);
            ScreenshotAnnotationSurface? surface = null;
            surface = new(Session, new(0, 0, region.Width, region.Height), 1, args =>
            {
                // ReSharper disable once AccessToModifiedClosure
                var point = args.GetPosition(surface!);
                return new((int)Math.Floor(point.X), (int)Math.Floor(point.Y));
            });
            return surface;
        }

        internal void DetachHostedSurface(Window owner)
        {
            _hostedOwners.Remove(owner);
            owner.RemoveHandler(InputElement.KeyDownEvent, HandleOwnedKeyDown);
            owner.RemoveHandler(InputElement.KeyUpEvent, HandleOwnedKeyUp);
            owner.Deactivated -= KeyboardOwnerDeactivated;
            ReleaseKeys();
        }
    }
}
