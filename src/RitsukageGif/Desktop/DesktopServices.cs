using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed class DesktopServices : IDisposable
    {
        private Window? _context;

        public event EventHandler? DisplaysChanged;

        // Avalonia's platform services require a TopLevel. This context is never shown.
        public Window Context
        {
            get
            {
                if (_context is not null) return _context;
                _context = new Window { ShowInTaskbar = false, ShowActivated = false, Width = 1, Height = 1 };
                _context.Screens.Changed += ScreensChanged;
                return _context;
            }
        }

        public Screens Screens => Context.Screens;
        private void ScreensChanged(object? sender, EventArgs args) => DisplaysChanged?.Invoke(this, args);

        public void Dispose()
        {
            if (_context is null) return;
            _context.Screens.Changed -= ScreensChanged;
            _context.Close();
            _context = null;
        }
    }
}
