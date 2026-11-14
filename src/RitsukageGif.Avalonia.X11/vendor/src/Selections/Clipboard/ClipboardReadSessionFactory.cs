using static Avalonia.X11.XLib;

namespace Avalonia.X11.Selections.Clipboard
{
    internal static class ClipboardReadSessionFactory
    {
        public static SelectionReadSession CreateSession(AvaloniaX11Platform platform)
        {
            var window = new EventStreamWindow(platform);
            XSelectInput(platform.Display, window.Handle, new((int)XEventMask.PropertyChangeMask));

            return new(
                platform.Display,
                window.Handle,
                platform.Info.Atoms.CLIPBOARD,
                window,
                platform.Info.Atoms);
        }
    }
}
