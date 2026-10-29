namespace RitsukageGif.Desktop
{
    internal static partial class PlatformHotkeys
    {
        internal static void Run((uint Modifiers, uint Key)[] keys, Action<int> pressed,
            Action<string> error, Action registered, CancellationToken token)
        {
            if (keys.All(key => key.Key == 0))
            {
                if (!token.IsCancellationRequested) registered();
                return;
            }

            if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                RunX11(keys, pressed, error, registered, token);
            else error(UiText.Get("PlatformGlobalHotkeysUnavailable"));
        }
    }
}
