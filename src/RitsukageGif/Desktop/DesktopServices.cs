using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed class DesktopServices : IDisposable
    {
        private WindowsColorChangeSubscription? _colorChanges;
        private Window? _context;

        // Avalonia's platform services require a TopLevel. This context is never shown.
        public Window Context
        {
            get
            {
                if (_context is not null) return _context;
                _context = new() { ShowInTaskbar = false, ShowActivated = false, Width = 1, Height = 1 };
                _context.Screens.Changed += ScreensChanged;
                if (OperatingSystem.IsWindows() && _context.TryGetPlatformHandle() is { } handle)
                    _colorChanges = new(handle.Handle);
                return _context;
            }
        }

        public Screens Screens => Context.Screens;

        public void Dispose()
        {
            if (_context is null) return;
            _context.Screens.Changed -= ScreensChanged;
            _colorChanges?.Dispose();
            _colorChanges = null;
            _context.Close();
            _context = null;
        }

        public event EventHandler? DisplaysChanged;

        private void ScreensChanged(object? sender, EventArgs args)
        {
            DisplaysChanged?.Invoke(this, args);
        }
    }
}
