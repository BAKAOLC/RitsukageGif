namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private string DescribeGlobalShortcut(string shortcut)
        {
            var key = HotkeyService.Parse(shortcut);
            foreach (var action in Settings.GlobalActions)
                if (action.Enabled && !string.IsNullOrWhiteSpace(action.Shortcut) &&
                    HotkeyService.Parse(action.Shortcut) == key)
                    return GlobalActions.Label(action);
            foreach (var preset in Settings.PresetHotkeys)
                if (HotkeyService.Parse(preset.Shortcut) == key)
                    return UiText.Get("GlobalActionRunPreset") + ": " + preset.Preset;
            var gesture = KeyGestureDisplay.Parse(shortcut);
            if (SelectionCommands.TryResolve(gesture.Key, gesture.KeyModifiers, out var definition))
                return UiText.Get(definition!.DescriptionKey);
            return UiText.Get("Shortcut") + ": " + KeyGestureDisplay.Format(shortcut);
        }
    }
}
