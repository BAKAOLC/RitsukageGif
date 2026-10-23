namespace RitsukageGif.Desktop
{
    internal static partial class PlatformHotkeys
    {
        internal static void Run((uint Modifiers, uint Key)[] keys, CancellationToken token, Action<int> pressed,
            Action<string> error)
        {
            if (keys.All(key => key.Key == 0)) return;
            if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection) RunX11(keys, token, pressed, error);
            else error(UiText.Get("PlatformGlobalHotkeysUnavailable"));
        }
    }
}
