using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class SelectionCommands
    {
        static SelectionCommands()
        {
            Catalog = Array.AsReadOnly<SelectionShortcutDefinition>([
                .. DefaultDefinitions
                    .GroupBy(item => item.Id)
                    .Select(group => new SelectionShortcutDefinition(group.Key, group.First().DescriptionKey,
                        group.First().Command is SelectionCommand.SelectTool or SelectionCommand.Undo
                            or SelectionCommand.Redo or SelectionCommand.Copy or SelectionCommand.Save
                            or SelectionCommand.Pin
                            or SelectionCommand.ScrollVertical or SelectionCommand.ScrollHorizontal
                            or SelectionCommand.RecognizeText or SelectionCommand.RecognizeQr
                            or SelectionCommand.RecognizeTextAndCopy
                            or SelectionCommand.PreviousScreenshot or SelectionCommand.NextScreenshot
                            or SelectionCommand.RefreshScreenshot
                            or SelectionCommand.ToggleAnnotations or SelectionCommand.ClearAnnotations
                            or SelectionCommand.RenumberAnnotations or SelectionCommand.FinishPolyline
                            ? "BuiltinShortcutScreenshotContext"
                            : "BuiltinShortcutSelectionContext",
                        Array.AsReadOnly<string>([
                            .. group.Select(item => item.Gesture),
                        ]),
                        group.First().DirectionKey)),
            ]);
        }

        internal static IReadOnlyList<SelectionShortcutDefinition> Catalog { get; }

        internal static IReadOnlyList<SelectionCommandDefinition> SnapshotBindings()
        {
            return Array.AsReadOnly(_definitions);
        }

        internal static IReadOnlyList<string> Validate(Dictionary<string, string[]>? overrides)
        {
            return [.. BuildBindings(overrides, out _).Select(issue => issue.Message)];
        }

        internal static IReadOnlyList<string> Configure(Dictionary<string, string[]>? overrides)
        {
            var errors = BuildBindings(overrides, out var definitions);
            if (errors.Count == 0)
            {
                _definitions = definitions;
                BuiltinShortcuts.ConfigureExternal(overrides);
            }

            return [.. errors.Select(issue => issue.Message)];
        }

        internal static IReadOnlyList<SelectionShortcutIssue> ValidateIssues(Dictionary<string, string[]>? overrides)
        {
            return BuildBindings(overrides, out _);
        }

        private static string ShortcutLabel(SelectionShortcutDefinition definition)
        {
            return UiText.Get(definition.DescriptionKey) + (definition.DirectionKey == Key.None
                ? string.Empty
                : " · " + UiText.Get("SelectionShortcut" + definition.DirectionKey));
        }

        private static List<SelectionShortcutIssue> BuildBindings(Dictionary<string, string[]>? overrides,
            out SelectionCommandDefinition[] definitions)
        {
            var errors = new List<SelectionShortcutIssue>(BuiltinShortcuts.ValidateExternal(overrides));
            var result = new List<SelectionCommandDefinition>();
            var gestures = new Dictionary<(Key, KeyModifiers), string>();
            foreach (var item in Catalog)
            {
                var defaults = DefaultDefinitions.Where(definition => definition.Id == item.Id).ToArray();
                var bindings = BuiltinShortcuts.SelectionGestures(item.Id, item.DefaultGestures, overrides);
                foreach (var value in bindings)
                {
                    if (value == string.Empty && defaults[0].Key == Key.None) continue;
                    KeyGesture gesture;
                    try
                    {
                        if (string.IsNullOrWhiteSpace(value)) throw new FormatException();
                        gesture = KeyGestureDisplay.Parse(value);
                        if (gesture.Key == Key.RightShift) gesture = new(Key.LeftShift, gesture.KeyModifiers);
                        if (!Enum.IsDefined(gesture.Key) || gesture.Key is Key.None or Key.LeftCtrl or Key.RightCtrl
                                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin ||
                            (gesture.KeyModifiers & ~(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt |
                                                      KeyModifiers.Meta)) != 0 ||
                            (gesture.Key is Key.LeftShift or Key.RightShift &&
                             (defaults[0].Command != SelectionCommand.CycleColorFormat ||
                              gesture.KeyModifiers != KeyModifiers.None)))
                            throw new FormatException();
                    }
                    catch (Exception error) when (error is ArgumentException or FormatException)
                    {
                        errors.Add(new(item.Id, null,
                            UiText.Get("SelectionShortcutInvalid") + ": " + ShortcutLabel(item) + " — " + value));
                        continue;
                    }

                    if (gestures.TryGetValue((gesture.Key, gesture.KeyModifiers), out var existing))
                    {
                        if (existing != item.Id)
                            errors.Add(new(item.Id, existing, UiText.Get("SelectionShortcutConflict") + ": " + value +
                                                              " — " +
                                                              ShortcutLabel(Catalog.First(definition =>
                                                                  definition.Id == existing)) + ", " +
                                                              ShortcutLabel(item)));
                        continue;
                    }

                    gestures.Add((gesture.Key, gesture.KeyModifiers), item.Id);
                    result.Add(defaults[0] with { Key = gesture.Key, Modifiers = gesture.KeyModifiers });
                }
            }

            definitions = [.. result];
            return errors;
        }
    }
}
