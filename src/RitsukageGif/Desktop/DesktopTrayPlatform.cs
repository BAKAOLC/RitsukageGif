using Avalonia;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed class DesktopTrayPlatform(IWindowingPlatform inner) : IWindowingPlatform
    {
        public IWindowImpl CreateWindow()
        {
            return inner.CreateWindow();
        }

        public ITopLevelImpl CreateEmbeddableTopLevel()
        {
            return inner.CreateEmbeddableTopLevel();
        }

        public IWindowImpl CreateEmbeddableWindow()
        {
            return inner.CreateEmbeddableWindow();
        }

        public void GetWindowsZOrder(ReadOnlySpan<IWindowImpl> windows, Span<long> order)
        {
            inner.GetWindowsZOrder(windows, order);
        }

        public ITrayIconImpl? CreateTrayIcon()
        {
            try
            {
                PortableTrayIcon? icon = OperatingSystem.IsMacOS() ? new MacTrayIcon() :
                    OperatingSystem.IsLinux() ? new LinuxTrayIcon() : null;
                if (icon is null) return inner.CreateTrayIcon();
                PortableTrayState.Attach(icon);
                return icon;
            }
            catch (Exception exception)
            {
                LogHub.GetLogger("Desktop.Tray")
                    .ZLogWarning(exception, $"Native tray backend could not be initialized.");
                return inner.CreateTrayIcon();
            }
        }

        internal static void Install()
        {
            if (OperatingSystem.IsWindows()) return;
            if (AvaloniaLocator.Current.GetService<IWindowingPlatform>() is { } platform and not DesktopTrayPlatform)
                AvaloniaLocator.CurrentMutable.Bind<IWindowingPlatform>().ToConstant(new DesktopTrayPlatform(platform));
        }
    }
}
