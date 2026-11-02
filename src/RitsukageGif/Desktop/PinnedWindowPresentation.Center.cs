using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        internal Avalonia.PixelPoint ScreenCenter => _owner.PointToScreen(new(_owner.ClientSize.Width / 2,
            _owner.ClientSize.Height / 2));

        internal void SetScreenCenter(Avalonia.PixelPoint target)
        {
            _owner.UpdateLayout();
            var current = ScreenCenter;
            _owner.Position = new(_owner.Position.X + target.X - current.X,
                _owner.Position.Y + target.Y - current.Y);
        }
    }
}
