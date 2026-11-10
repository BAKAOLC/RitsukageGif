using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static partial class GlobalActions
    {
        internal static string Label(GlobalActionBinding action)
        {
            return string.IsNullOrWhiteSpace(action.Name) ? UiText.Get("GlobalAction" + action.Kind) : action.Name;
        }

        internal static bool IsScreenshot(GlobalActionKind kind)
        {
            return Definitions.TryGetValue(kind, out var definition) && definition.IsScreenshot;
        }

        internal static bool IsDirectScreenshot(GlobalActionKind kind)
        {
            return Definitions.TryGetValue(kind, out var definition) && definition.DirectScreenshot;
        }

        internal static ActionScreenshotOutput ScreenshotOutput(GlobalActionBinding action)
        {
            var value = Definition(action.Kind).ScreenshotOutput ?? ActionScreenshotOutput.Copy;
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
            _ = ScreenshotGeometry(action);
            foreach (var (key, value) in action.Arguments)
                switch (key)
                {
                    case "output" when IsDirectScreenshot(action.Kind): break;
                    case "delay": _ = ScreenshotDelay(action); break;
                    case var _ when IsScreenshotGeometryArgument(key): break;
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

                if (action.IgnoredPrograms.Any(rule => !HotkeyPolicyPreferences.TryNormalizeProgram(rule, out _)))
                    errors.Add(label + ": " + UiText.Get("HotkeyIgnoredProgramInvalid"));
                try
                {
                    var gesture = HotkeyService.Parse(action.Shortcut);
                    if (gesture.Key != 0 && !shortcuts.TryAdd(gesture, label))
                        errors.Add(label + ": " + UiText.Get("DuplicateHotkeys") + " — " + shortcuts[gesture]);
                    if (IsScreenshot(action.Kind)) ValidateScreenshot(action);
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (action.Kind)
                    {
                        case GlobalActionKind.PinFile:
                            if (string.IsNullOrWhiteSpace(action.Parameter))
                                errors.Add(label + ": " + UiText.Get("GlobalActionFileRequired"));
                            else _ = Path.GetFullPath(action.Parameter);
                            break;
                        case GlobalActionKind.RunScript:
                            _ = ActionScriptCompilation.Compile(action.Script);
                            break;
                        case GlobalActionKind.RunPreset:
                            if (string.IsNullOrWhiteSpace(action.Parameter))
                                errors.Add(label + ": " + UiText.Get("GlobalActionFileRequired"));
                            _ = ScriptParameters.Parse(action.Arguments.Select(pair => pair.Key + "=" + pair.Value));
                            break;
                    }
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException
                                                  or PathTooLongException or PresetSyntaxException)
                {
                    errors.Add(label + ": " + error.Message);
                }
            }

            return errors;
        }
    }
}
