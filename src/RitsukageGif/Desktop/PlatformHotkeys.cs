namespace RitsukageGif.Desktop
{
    internal static partial class PlatformHotkeys
    {
        internal static void Run((uint Modifiers, uint Key)[] keys, Action<int> pressed,
            Action<string> error, Action registered, CancellationToken token,
            Action<int, string?>? registrationChanged = null, Action<int, string>? triggerChanged = null,
            IReadOnlyList<string>? descriptions = null)
        {
            if (keys.All(key => key.Key == 0))
            {
                if (!token.IsCancellationRequested) registered();
                return;
            }

            if (OperatingSystem.IsLinux() && DesktopCapture.UsesSourceSelection)
            {
                RunWayland(keys, pressed, error, registered, registrationChanged, triggerChanged, token, descriptions);
            }
            else if (OperatingSystem.IsLinux())
            {
                RunX11(keys, pressed, error, registered, registrationChanged, token);
            }
            else
            {
                var message = UiText.Get("PlatformGlobalHotkeysUnavailable");
                for (var index = 0; index < keys.Length; index++)
                    if (keys[index].Key != 0)
                        registrationChanged?.Invoke(index, message);
                error(message);
            }
        }
    }
}
