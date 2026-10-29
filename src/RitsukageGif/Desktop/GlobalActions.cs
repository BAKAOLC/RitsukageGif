using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static class GlobalActions
    {
        internal static string Label(GlobalActionBinding action)
        {
            return string.IsNullOrWhiteSpace(action.Name) ? UiText.Get("GlobalAction" + action.Kind) : action.Name;
        }

        internal static bool IsScreenshot(GlobalActionKind kind)
        {
            return kind is GlobalActionKind.Screenshot
                or GlobalActionKind.ScreenshotAndCopy or GlobalActionKind.ScreenshotAndPin
                or GlobalActionKind.ScreenshotAndSave or GlobalActionKind.ScreenshotQuickSave
                or GlobalActionKind.CaptureActiveWindow or GlobalActionKind.CaptureLastRegion
                or GlobalActionKind.ScreenshotAndCopyText or GlobalActionKind.ScreenshotAndScroll;
        }

        internal static bool IsDirectScreenshot(GlobalActionKind kind)
        {
            return kind is
                GlobalActionKind.CaptureActiveWindow or GlobalActionKind.CaptureLastRegion;
        }

        internal static ActionScreenshotOutput ScreenshotOutput(GlobalActionBinding action)
        {
            var value = action.Kind switch
            {
                GlobalActionKind.ScreenshotAndPin => ActionScreenshotOutput.Pin,
                GlobalActionKind.ScreenshotAndSave => ActionScreenshotOutput.Save,
                GlobalActionKind.ScreenshotQuickSave => ActionScreenshotOutput.QuickSave,
                GlobalActionKind.ScreenshotAndCopyText => ActionScreenshotOutput.CopyText,
                _ => ActionScreenshotOutput.Copy,
            };
            if (!IsDirectScreenshot(action.Kind) || !action.Arguments.TryGetValue("output", out var output))
                return value;
            if (Enum.TryParse<ActionScreenshotOutput>(output, true, out var parsed) && Enum.IsDefined(parsed) &&
                string.Equals(parsed.ToString(), output, StringComparison.OrdinalIgnoreCase)) return parsed;
            throw new ArgumentException(UiText.Get("GlobalActionInvalidOutput"));
        }

        private static void ValidateScreenshot(GlobalActionBinding action)
        {
            if (action.Parameter is not ("" or "screen" or "window"))
                throw new ArgumentException(UiText.Get("GlobalActionInvalidSource"));
            var output = ScreenshotOutput(action);
            foreach (var (key, value) in action.Arguments)
                switch (key)
                {
                    case "output" when IsDirectScreenshot(action.Kind): break;
                    case "delay": _ = ScreenshotDelay(action); break;
                    case "directory" when output == ActionScreenshotOutput.QuickSave:
                        if (!string.IsNullOrWhiteSpace(value)) _ = Path.GetFullPath(value);
                        break;
                    default: throw new ArgumentException(UiText.Get("GlobalActionInvalidOutput"));
                }
        }

        internal static TimeSpan ScreenshotDelay(GlobalActionBinding action)
        {
            if (!action.Arguments.TryGetValue("delay", out var value)) return TimeSpan.Zero;
            if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out var seconds) ||
                !double.IsFinite(seconds) || seconds is < 0 or > 86400)
                throw new ArgumentException(UiText.Get("GlobalActionInvalidDelay"));
            return TimeSpan.FromSeconds(seconds);
        }

        // ReSharper disable once UnusedParameter.Global
        internal static IReadOnlyList<string> Validate(IReadOnlyList<GlobalActionBinding> actions,
            IEnumerable<string> reservedShortcuts, IEnumerable<GlobalMouseBinding>? mouseBindings = null)
        {
            var errors = new List<string>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var shortcuts = new Dictionary<(uint, uint), string>();
            foreach (var shortcut in reservedShortcuts)
                try
                {
                    var parsed = HotkeyService.Parse(shortcut);
                    if (parsed.Key != 0) shortcuts.TryAdd(parsed, UiText.Get("GlobalHotkeysHeading"));
                }
                catch (ArgumentException error)
                {
                    errors.Add(error.Message);
                }

            foreach (var action in actions)
            {
                var label = string.IsNullOrWhiteSpace(action.Name)
                    ? UiText.Get("GlobalAction" + action.Kind)
                    : action.Name;
                if (string.IsNullOrWhiteSpace(action.Id) || !identifiers.Add(action.Id))
                    errors.Add(label + ": " + UiText.Get("GlobalActionInvalidIdentity"));
                if (!action.Enabled) continue;
                if (!Enum.IsDefined(action.Kind))
                {
                    errors.Add(label + ": " + UiText.Get("GlobalActionInvalidKind"));
                    continue;
                }

                try
                {
                    var gesture = HotkeyService.Parse(action.Shortcut);
                    if (gesture.Key != 0 && !shortcuts.TryAdd(gesture, label))
                        errors.Add(label + ": " + UiText.Get("DuplicateHotkeys") + " — " + shortcuts[gesture]);
                    if (IsScreenshot(action.Kind)) ValidateScreenshot(action);
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (action.Kind)
                    {
                        case GlobalActionKind.RunPreset:
                            if (string.IsNullOrWhiteSpace(action.Parameter))
                                errors.Add(label + ": " + UiText.Get("GlobalActionFileRequired"));
                            _ = ScriptParameters.Parse(action.Arguments.Select(pair => pair.Key + "=" + pair.Value));
                            break;
                    }
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException
                                                  or PathTooLongException)
                {
                    errors.Add(label + ": " + error.Message);
                }
            }

            return errors;
        }
    }
}
