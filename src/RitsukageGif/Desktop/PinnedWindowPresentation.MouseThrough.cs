using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        private PinnedMouseThroughToolbar? _mouseThroughToolbar;

        internal void RefreshMouseThroughToolbar()
        {
            if (!_pin.IsMouseThrough || !_preferences().ShowMouseThroughToolbar)
            {
                CloseMouseThroughToolbar();
                return;
            }

            if (!_owner.IsVisible || _owner.WindowState == WindowState.Minimized)
            {
                _mouseThroughToolbar?.Hide();
                return;
            }

            if (_mouseThroughToolbar is null)
            {
                var toolbar = new PinnedMouseThroughToolbar(_pin, () => _ = RunOperationAsync(() =>
                {
                    _pin.SetMouseThrough(false);
                    return Task.CompletedTask;
                }));
                _mouseThroughToolbar = toolbar;
                toolbar.Closed += (_, _) => _mouseThroughToolbar = null;
            }

            _mouseThroughToolbar.Reveal();
        }

        private void CloseMouseThroughToolbar()
        {
            _mouseThroughToolbar?.Close();
        }
    }
}
