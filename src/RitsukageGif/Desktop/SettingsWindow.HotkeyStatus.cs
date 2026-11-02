namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        internal Func<GlobalActionBinding, HotkeyRegistrationResult?>? HotkeyRegistration { private get; init; }
        internal Func<string, string, HotkeyRegistrationResult?>? GlobalMouseRegistration { private get; init; }
        private event Action? HotkeyRegistrationsChanged;

        internal void RefreshHotkeyRegistrations()
        {
            HotkeyRegistrationsChanged?.Invoke();
        }
    }
}
