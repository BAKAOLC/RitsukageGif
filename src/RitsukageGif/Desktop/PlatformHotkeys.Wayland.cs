using System.Text;

namespace RitsukageGif.Desktop
{
    internal static partial class PlatformHotkeys
    {
        private static void RunWayland((uint Modifiers, uint Key)[] keys, Action<int> pressed,
            Action<string> error, Action registered, Action<int, string?>? registrationChanged,
            Action<int, string>? triggerChanged, CancellationToken token, IReadOnlyList<string>? descriptions = null)
        {
            RunPortableHotkeysAsync(keys, pressed, error, registered, registrationChanged, triggerChanged,
                    "RitsukageGif.Shortcuts", true, descriptions, token)
                .GetAwaiter().GetResult();
        }

        private static string PortalTrigger((uint Modifiers, uint Key) key)
        {
            var value = new StringBuilder();
            if ((key.Modifiers & 2) != 0) value.Append("CTRL+");
            if ((key.Modifiers & 1) != 0) value.Append("ALT+");
            if ((key.Modifiers & 4) != 0) value.Append("SHIFT+");
            if ((key.Modifiers & 8) != 0) value.Append("LOGO+");
            return value.Append(HotkeyKeyMap.X11Name(key.Key)).ToString();
        }
    }
}
