using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class DesktopDisplayState : IDisposable
    {
        private readonly Screens _screens;
        private int _disposed;
        private DisplayTopology _snapshot;

        public DesktopDisplayState(Screens screens)
        {
            Dispatcher.UIThread.VerifyAccess();
            _screens = screens;
            _snapshot = ReadTopology(0);
            _screens.Changed += ScreensChanged;
        }

        private DisplayTopology Snapshot => Volatile.Read(ref _snapshot);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (Dispatcher.UIThread.CheckAccess()) _screens.Changed -= ScreensChanged;
            else Dispatcher.UIThread.Post(() => _screens.Changed -= ScreensChanged);
        }

        public DisplayTopology Read()
        {
            return Snapshot;
        }

        private DisplayTopology ReadTopology(long version)
        {
            return new(_screens.All.Select(screen =>
                new PixelRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height)), version);
        }

        private void ScreensChanged(object? sender, EventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess()) Publish();
            else Dispatcher.UIThread.Post(Publish);
        }

        private void Publish()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var current = Snapshot;
            var next = ReadTopology(current.Version + 1);
            if (!current.Monitors.SequenceEqual(next.Monitors)) Volatile.Write(ref _snapshot, next);
        }
    }
}
