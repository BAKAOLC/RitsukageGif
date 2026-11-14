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
                        PinnedAction(group.First().Command) is not null
                            ? BuiltinShortcutContexts.CapturePin
                            : group.First().Command is SelectionCommand.SelectTool or SelectionCommand.Undo
                                or SelectionCommand.Redo
                                or SelectionCommand.Print
                                or SelectionCommand.Pin or SelectionCommand.WindowPin or SelectionCommand.Record
                                or SelectionCommand.ScrollVertical or SelectionCommand.ScrollHorizontal
                                or SelectionCommand.RecognizeText or SelectionCommand.RecognizeQr
                                or SelectionCommand.RecognizeTable or SelectionCommand.RecognizeFormula
                                or SelectionCommand.PreviousScreenshot or SelectionCommand.NextScreenshot
                                or SelectionCommand.RefreshScreenshot
                                or SelectionCommand.ToggleAnnotations or SelectionCommand.ToggleSecondaryEditing
                                or SelectionCommand.ToggleAnnotationToolbar or SelectionCommand.Translate
                                or SelectionCommand.SimulateWheelUp or SelectionCommand.SimulateWheelDown
                                or SelectionCommand.ToggleCoordinateMode
                                or SelectionCommand.ClearAnnotations
                                or SelectionCommand.RenumberAnnotations or SelectionCommand.FinishPolyline
                                or SelectionCommand.TogglePolylineArrows
                                ? "BuiltinShortcutScreenshotContext"
                                : "BuiltinShortcutSelectionContext",
                        Array.AsReadOnly<string>([
                            .. group.Select(item => item.Gesture),
                        ]),
                        group.First().DirectionKey)),
            ]);
        }

        internal static IReadOnlyList<SelectionShortcutDefinition> Catalog { get; }

        internal static string? PinnedAction(SelectionCommand command)
        {
            // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
            return command switch
            {
                SelectionCommand.Cancel => "close",
                SelectionCommand.Copy => "copy",
                SelectionCommand.Save => "save",
                SelectionCommand.QuickSave => "quick-save",
                SelectionCommand.RecognizeTextAndCopy => "copy-all-text",
                _ => null,
            };
        }

        internal static event Action? BindingsChanged;

        internal static IReadOnlyList<SelectionCommandDefinition> SnapshotBindings()
        {
            return _definitions;
        }

        internal static IReadOnlyList<string> Validate(Dictionary<string, string[]>? overrides, bool mixing = false)
        {
            return [.. BuildBindings(overrides, out _, mixing).Select(issue => issue.Message)];
        }

        internal static IReadOnlyList<string> Configure(Dictionary<string, string[]>? overrides, bool mixing = false)
        {
            var errors = BuildBindings(overrides, out var definitions, mixing);
            if (errors.Count == 0)
            {
                _definitions = Array.AsReadOnly(definitions);
                BuiltinShortcuts.ConfigureExternal(overrides, mixing);
                BuiltinKeys.Configure(mixing);
                BindingsChanged?.Invoke();
            }

            return [.. errors.Select(issue => issue.Message)];
        }

        internal static IReadOnlyList<SelectionShortcutIssue> ValidateIssues(Dictionary<string, string[]>? overrides,
            bool mixing = false)
        {
            return BuildBindings(overrides, out _, mixing);
        }

        internal static void CollectAnnotationOverrides(Dictionary<string, string[]>? settings, bool mixing,
            List<BuiltinShortcutDefaultOverride> overrides)
        {
            BuildBindings(settings, out _, mixing, overrides);
        }

        private static string ShortcutLabel(SelectionShortcutDefinition definition)
        {
            return definition.DirectionKey == Key.None
                ? UiText.Get(definition.DescriptionKey)
                : UiText.Format("BuiltinShortcutDirectionalOperation",
                    UiText.Get("SelectionShortcut" + definition.DirectionKey),
                    UiText.Get(definition.DescriptionKey));
        }

        private static List<SelectionShortcutIssue> BuildBindings(Dictionary<string, string[]>? overrides,
            out SelectionCommandDefinition[] definitions, bool mixing,
            List<BuiltinShortcutDefaultOverride>? annotationOverrides = null)
        {
            var errors = new List<SelectionShortcutIssue>(BuiltinShortcuts.ValidateExternal(overrides, mixing));
            var result = new List<SelectionCommandDefinition>();
            var gestures = new Dictionary<(Key, KeyModifiers), (string Id, SelectionCommand Command)>();
            foreach (var item in Catalog)
            {
                var defaults = DefaultDefinitions.Where(definition => definition.Id == item.Id).ToArray();
                var bindings = BuiltinShortcuts.SelectionGestures(item.Id, item.DefaultGestures, overrides);
                foreach (var (value, slot) in bindings)
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

                    var key = BuiltinKeys.Normalize(gesture.Key, mixing);
                    if (gestures.TryGetValue((key, gesture.KeyModifiers), out var existing))
                    {
                        if (existing.Id == item.Id) continue;
                        var disjoint = (existing.Command, defaults[0].Command) is
                            (SelectionCommand.ToggleControls, SelectionCommand.TogglePolylineArrows) or
                            (SelectionCommand.TogglePolylineArrows, SelectionCommand.ToggleControls);
                        if (!disjoint)
                        {
                            errors.Add(new(item.Id, existing.Id,
                                UiText.Get("SelectionShortcutConflict") + ": " + value + " — " +
                                ShortcutLabel(Catalog.First(definition => definition.Id == existing.Id)) + ", " +
                                ShortcutLabel(item)));
                            continue;
                        }
                    }
                    else
                    {
                        gestures.Add((key, gesture.KeyModifiers), (item.Id, defaults[0].Command));
                    }

                    result.Add(defaults[slot] with { Key = gesture.Key, Modifiers = gesture.KeyModifiers });
                }
            }

            definitions = [.. result];
            errors.AddRange(
                BuiltinShortcuts.ValidateAnnotationBindings(definitions, overrides, mixing, annotationOverrides));
            return errors;
        }
    }
}
