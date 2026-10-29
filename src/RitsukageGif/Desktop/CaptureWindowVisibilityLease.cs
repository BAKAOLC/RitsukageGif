using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed class CaptureWindowVisibilityLease : IDisposable
    {
        private readonly Func<bool> _canRestore;
        private readonly Window? _owner;
        private readonly bool _wasVisible;
        private readonly Window? _window;
        private bool _closed;
        private bool _disposed;
        private bool _ownerClosed;

        private CaptureWindowVisibilityLease(Window? window, Func<bool> canRestore)
        {
            _window = window;
            _owner = window?.Owner as Window;
            _canRestore = canRestore;
            _wasVisible = window?.IsVisible == true;
            if (window is not null) window.Closed += WindowClosed;
            if (_owner is not null) _owner.Closed += OwnerClosed;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_owner is not null) _owner.Closed -= OwnerClosed;
            if (_window is null) return;
            _window.Closed -= WindowClosed;
            if (!_wasVisible || _closed || _ownerClosed || _window.IsVisible || !_canRestore()) return;
            var activate = _window.ShowActivated;
            _window.ShowActivated = false;
            try
            {
                if (_owner is null) _window.Show();
                else _window.Show(_owner);
            }
            finally
            {
                _window.ShowActivated = activate;
            }
        }

        internal static async Task<CaptureWindowVisibilityLease> HideAsync(Window? window,
            Func<bool> canRestore, CancellationToken token)
        {
            var lease = new CaptureWindowVisibilityLease(window, canRestore);
            try
            {
                if (lease._wasVisible) await CaptureWindowVisibility.HideAsync(window, token);
                token.ThrowIfCancellationRequested();
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        private void WindowClosed(object? sender, EventArgs args)
        {
            _closed = true;
        }

        private void OwnerClosed(object? sender, EventArgs args)
        {
            _ownerClosed = true;
        }
    }
}
