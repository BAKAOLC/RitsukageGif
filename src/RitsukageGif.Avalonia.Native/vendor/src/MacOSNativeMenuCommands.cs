using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    internal class MacOsNativeMenuCommands : INativeApplicationCommands
    {

        private readonly IAvnApplicationCommands _commands;
        public static readonly AttachedProperty<bool> IsServicesSubmenuProperty =
            AvaloniaProperty.RegisterAttached<MacOsNativeMenuCommands, NativeMenu, bool>("IsServicesSubmenu");

        // ReSharper disable once ConvertToPrimaryConstructor
        public MacOsNativeMenuCommands(IAvnApplicationCommands commands)
        {
            _commands = commands;
        }

        public void ShowApp()
        {
            _commands.UnhideApp();
        }

        public void HideApp()
        {
            _commands.HideApp();
        }

        public void ShowAll()
        {
            _commands.ShowAll();
        }

        public void HideOthers()
        {
            _commands.HideOthers();
        }
    }
}
