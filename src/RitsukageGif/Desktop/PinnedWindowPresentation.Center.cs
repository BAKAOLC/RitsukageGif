using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        internal Avalonia.PixelPoint ScreenCenter => _owner.PointToScreen(new(_owner.ClientSize.Width / 2,
            _owner.ClientSize.Height / 2));

        internal static Avalonia.PixelPoint? ResolveNewPinCenter(PinnedDisplayPosition placement, Screens screens,
            Func<Avalonia.PixelPoint?> readPointer)
        {
            var pointer = placement switch
            {
                PinnedDisplayPosition.PrimaryScreenCenter => null,
                PinnedDisplayPosition.ScreenshotLocation or PinnedDisplayPosition.MouseLocation or
                    PinnedDisplayPosition.MouseScreenCenter => readPointer(),
                _ => throw new ArgumentOutOfRangeException(nameof(placement)),
            };
            if (placement == PinnedDisplayPosition.MouseLocation && pointer is { } mouse) return mouse;
            var screen = pointer is { } point ? screens.ScreenFromPoint(point) ?? screens.Primary : screens.Primary;
            return screen?.Bounds.Center;
        }

        internal void SetScreenCenter(Avalonia.PixelPoint target)
        {
            _owner.UpdateLayout();
            var current = ScreenCenter;
            _owner.Position = new(_owner.Position.X + target.X - current.X,
                _owner.Position.Y + target.Y - current.Y);
        }
    }
}
