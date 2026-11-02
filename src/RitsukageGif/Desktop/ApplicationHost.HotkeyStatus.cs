namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly Dictionary<(uint Modifiers, uint Key), HotkeyRegistrationResult> _hotkeyRegistrations = [];

        private HotkeyRegistrationResult? GetActionHotkeyRegistration(GlobalActionBinding action)
        {
            var reason = GlobalInputDisabledReason(action, _registeredInputContext ?? ReadGlobalInputContext(),
                action.Kind == GlobalActionKind.ToggleGlobalShortcuts);
            return reason is null
                ? GetHotkeyRegistration(action.Shortcut)
                : new(action.Shortcut, HotkeyRegistrationState.Disabled, reason);
        }

        private HotkeyRegistrationResult? GetHotkeyRegistration(string shortcut)
        {
            return _hotkeyRegistrations.GetValueOrDefault(HotkeyService.Parse(shortcut));
        }

        private void ResetHotkeyRegistrations()
        {
            _hotkeyRegistrations.Clear();
            _settingsWindow?.RefreshHotkeyRegistrations();
        }

        private void SetHotkeyRegistration(HotkeyRegistrationResult result)
        {
            _hotkeyRegistrations[HotkeyService.Parse(result.Shortcut)] = result;
            _settingsWindow?.RefreshHotkeyRegistrations();
        }

        private void SetHotkeyDisabled(string shortcut, string reason)
        {
            if (!string.IsNullOrWhiteSpace(shortcut))
                SetHotkeyRegistration(new(shortcut, HotkeyRegistrationState.Disabled, reason));
        }

        private void FailPendingHotkeyRegistrations(string message)
        {
            foreach (var (key, result) in _hotkeyRegistrations)
                if (result.State == HotkeyRegistrationState.Pending)
                    _hotkeyRegistrations[key] = result with
                    {
                        State = HotkeyRegistrationState.Failed, Error = message,
                    };
            _settingsWindow?.RefreshHotkeyRegistrations();
        }
    }
}
