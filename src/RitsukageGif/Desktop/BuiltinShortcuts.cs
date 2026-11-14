using System.Collections.ObjectModel;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class BuiltinShortcuts
    {
        private static readonly Lazy<IReadOnlyList<BuiltinShortcutDefinition>> Definitions = new(BuildCatalog);
        private static IReadOnlyList<BuiltinShortcutBinding> _bindings;

        static BuiltinShortcuts()
        {
            _bindings = BuildExternal(null, out _);
        }

        private static IReadOnlyList<BuiltinShortcutDefinition> Catalog => Definitions.Value;

        internal static IReadOnlyList<string> ContextKeys =>
        [
            .. BuiltinShortcutContexts.Keys
                .Where(context => Catalog.Any(definition => definition.ContextKey == context)),
        ];

        internal static IReadOnlyList<BuiltinShortcutDefinition> Registrations()
        {
            return Catalog;
        }

        private static string SelectionSlotId(string id, int slot)
        {
            return $"builtin.selection.{id}.{slot}";
        }

        internal static string GetGesture(BuiltinShortcutDefinition definition, Dictionary<string, string[]>? settings)
        {
            if (ReadSetting(settings, definition.Id, out var value))
                return GestureAt(value, 0) ?? "";
            return definition.DefaultGesture;
        }

        internal static List<(string Gesture, int Slot)> SelectionGestures(string id, IReadOnlyList<string> defaults,
            Dictionary<string, string[]>? settings)
        {
            var result = new List<(string Gesture, int Slot)>();
            for (var index = 0; index < defaults.Count; index++)
                if (ReadSetting(settings, SelectionSlotId(id, index), out var slot))
                    foreach (var gesture in slot ?? [])
                        result.Add((gesture, index));
                else if (defaults[index].Length > 0) result.Add((defaults[index], index));
            return result;
        }

        internal static IReadOnlyList<SelectionShortcutIssue> ValidateExternal(Dictionary<string, string[]>? settings,
            bool mixing = false)
        {
            BuildExternal(settings, out var issues, mixing);
            return issues;
        }

        internal static void ConfigureExternal(Dictionary<string, string[]>? settings, bool mixing = false)
        {
            var bindings = BuildExternal(settings, out var issues, mixing);
            if (issues.Count == 0) _bindings = bindings;
        }

        internal static IReadOnlyList<BuiltinShortcutBinding> Snapshot()
        {
            return _bindings;
        }

        internal static IReadOnlyList<BuiltinShortcutDefaultOverride> DefaultOverrides(
            Dictionary<string, string[]>? settings, bool mixing)
        {
            var overrides = new List<BuiltinShortcutDefaultOverride>();
            BuildExternal(settings, out _, mixing, overrides);
            SelectionCommands.CollectAnnotationOverrides(settings, mixing, overrides);
            return overrides;
        }

        internal static string? Resolve(IReadOnlyList<BuiltinShortcutBinding> bindings, string context,
            KeyEventArgs args)
        {
            if (args.Handled || BuiltinShortcutTextInput.OwnsKey(args)) return null;
            foreach (var binding in bindings)
                if (binding.ContextKey == context && BuiltinKeys.Matches(binding.Gesture.Key, args.Key) &&
                    binding.Gesture.KeyModifiers == args.KeyModifiers)
                    return binding.Action;
            return null;
        }

        private static string? GestureAt(string[]? values, int index)
        {
            return values is not null && index >= 0 && index < values.Length ? values[index] : null;
        }

        private static bool ReadSetting(Dictionary<string, string[]>? settings, string id, out string[]? values)
        {
            values = null;
            return settings is not null && settings.TryGetValue(id, out values);
        }

        private static ReadOnlyCollection<BuiltinShortcutDefinition> BuildCatalog()
        {
            var result = new List<BuiltinShortcutDefinition>();
            foreach (var definition in SelectionCommands.Catalog)
                for (var index = 0; index < definition.DefaultGestures.Count; index++)
                    result.Add(new(SelectionSlotId(definition.Id, index), definition.CategoryKey,
                        definition.DescriptionKey,
                        definition.DefaultGestures[index], definition.Id, definition.Id,
                        definition.DirectionKey));
            result.AddRange(ExternalDefinitions());
            result.AddRange(PinLayoutDefinitions());
            return result.AsReadOnly();
        }

        private static BuiltinShortcutDefinition[] ExternalDefinitions()
        {
            return
            [
                new("builtin.pinned.image.brightness-increase", BuiltinShortcutContexts.PinnedImage,
                    "PinnedImageBrightnessIncrease", "7", "brightness-increase"),
                new("builtin.pinned.image.brightness-decrease", BuiltinShortcutContexts.PinnedImage,
                    "PinnedImageBrightnessDecrease", "8", "brightness-decrease"),
                new("builtin.pinned.image.reset-processing", BuiltinShortcutContexts.PinnedImage,
                    "PinnedProcessingReset", "0", "reset-processing"),
                new("builtin.pinned.image.opacity-increase", BuiltinShortcutContexts.PinnedImage,
                    "PinnedImageOpacityIncrease", "]", "opacity-increase"),
                new("builtin.pinned.image.opacity-decrease", BuiltinShortcutContexts.PinnedImage,
                    "PinnedImageOpacityDecrease", "[", "opacity-decrease"),
                new("builtin.pinned.image.select-all", BuiltinShortcutContexts.PinnedImage,
                    "PinnedTextSelectAll", "Ctrl+A", "select-all-ocr"),
                new("builtin.pinned.image.text-selectable", BuiltinShortcutContexts.PinnedImage,
                    "PinnedImageOcrTextSelectable", "E", "ocr-text-selectable"),
                new("builtin.pinned.image.qr", BuiltinShortcutContexts.PinnedImage,
                    "RecognizeBarcode", "Shift+D", "recognize-barcode"),
                new("builtin.pinned.image.translate", BuiltinShortcutContexts.PinnedImage, "PinnedInlineTranslate",
                    "Ctrl+Q", "translate-inline"),
                new("builtin.pinned.image.table", BuiltinShortcutContexts.PinnedImage, "PinnedMenuFormRecognition",
                    "Shift+Q", "recognize-table"),
                new("builtin.pinned.image.formula", BuiltinShortcutContexts.PinnedImage, "FormulaRecognition",
                    "Shift+F", "recognize-formula"),
                new("builtin.pinned.text.selection", "BuiltinShortcutPinnedTextContext", "PinnedTextSelectable", "",
                    "text-selection"),
                new("builtin.pinned.text.ignore-format", "BuiltinShortcutPinnedTextContext", "PinnedTextIgnoreFormat",
                    "", "text-ignore-format"),
                new("builtin.pinned.text.select-all", "BuiltinShortcutPinnedTextContext", "PinnedTextSelectAll", "",
                    "text-select-all"),
                new("builtin.pinned.wheel-up", "BuiltinShortcutPinnedContext", "BuiltinShortcutWheelUp", "=",
                    "wheel-up"),
                new("builtin.pinned.wheel-down", "BuiltinShortcutPinnedContext", "BuiltinShortcutWheelDown", "-",
                    "wheel-down"),
                new("builtin.pinned.title", "BuiltinShortcutPinnedContext", "PinnedTitleEdit", "F2", "set-title"),
                new("builtin.pinned.hide-others", "BuiltinShortcutPinnedContext", "PinnedHideOthers", "V",
                    "hide-others"),
                new("builtin.pinned.switch-next", "BuiltinShortcutPinnedContext", "PinnedSwitchNext", "Tab",
                    "switch-next"),
                new("builtin.pinned.new-group", "BuiltinShortcutPinnedContext", "PinnedImageNewGroup", "G",
                    "new-group"),
                new("builtin.dynamic.toggle", BuiltinShortcutContexts.PinnedDynamicImage, "PinnedAnimationToggle",
                    "Space", "animation-toggle"),
                new("builtin.dynamic.next", BuiltinShortcutContexts.PinnedDynamicImage, "PinnedAnimationNext", "Right",
                    "animation-next"),
                new("builtin.dynamic.previous", BuiltinShortcutContexts.PinnedDynamicImage, "PinnedAnimationPrevious",
                    "Left", "animation-previous"),
                new("builtin.dynamic.speed-up", BuiltinShortcutContexts.PinnedDynamicImage, "PinnedAnimationSpeedUp",
                    "Up", "animation-speed-up"),
                new("builtin.dynamic.speed-down", BuiltinShortcutContexts.PinnedDynamicImage,
                    "PinnedAnimationSpeedDown", "Down", "animation-speed-down"),
                new("builtin.dynamic.speed-reset", BuiltinShortcutContexts.PinnedDynamicImage,
                    "PinnedAnimationSpeedReset", "R", "animation-speed-reset"),
                new("builtin.window-roi.convert", BuiltinShortcutContexts.PinnedWindowRoi, "PinnedLiveConvert", "P",
                    "live-convert"),
                new("builtin.window-roi.rotate-clockwise", BuiltinShortcutContexts.PinnedWindowRoi,
                    "PinnedImageRotateClockwise", "1", "live-rotate-clockwise"),
                new("builtin.window-roi.rotate-counterclockwise", BuiltinShortcutContexts.PinnedWindowRoi,
                    "PinnedImageRotateCounterclockwise", "2", "live-rotate-counterclockwise"),
                new("builtin.window-roi.flip-horizontal", BuiltinShortcutContexts.PinnedWindowRoi,
                    "AnimationFlipHorizontal", "3", "live-flip-horizontal"),
                new("builtin.window-roi.flip-vertical", BuiltinShortcutContexts.PinnedWindowRoi,
                    "AnimationFlipVertical", "4", "live-flip-vertical"),
                new("builtin.window-roi.reset", BuiltinShortcutContexts.PinnedWindowRoi, "MouseActionReset", "0",
                    "live-reset"),
                new("builtin.pinned.window-shade", "BuiltinShortcutPinnedContext", "PinnedMenuWindowShade", "Y",
                    "window-shade"),
                new("builtin.pinned.configuration", "BuiltinShortcutPinnedContext", "PinnedConfiguration",
                    "Ctrl+Shift+P",
                    "configuration"),
                new("builtin.pinned.paste", "BuiltinShortcutPinnedContext", "BuiltinShortcutPinnedPaste", "Ctrl+V",
                    "paste"),
                new("builtin.pinned.lock", "BuiltinShortcutPinnedContext", "PinnedImageLock", "L", "lock"),
                new("builtin.pinned.topmost", "BuiltinShortcutPinnedContext", "PinnedImageTopmost", "T", "topmost"),
                new("builtin.pinned.through", "BuiltinShortcutPinnedContext", "PinnedImageMouseThrough", "", "through"),
                new("builtin.longshot.start-stop", "BuiltinShortcutLongshotContext", "ScrollCaptureStartStop", "Alt+4",
                    "start-stop"),
                new("builtin.longshot.toggleaxis", "BuiltinShortcutLongshotContext", "BuiltinShortcutToggleAxis", "",
                    "toggleaxis"),
                new("builtin.longshot.automatic", "BuiltinShortcutLongshotContext", "ScreenshotAutoScrolling", "Alt+1",
                    "automatic"),
                new("builtin.pinned.hide", "BuiltinShortcutPinnedContext", "PinnedImageHide", "", "hide"),
                new("builtin.pinned.print", "BuiltinShortcutPinnedContext", "Print", "Ctrl+P", "print"),
                new("builtin.pinned.annotate", "BuiltinShortcutPinnedContext", "PinnedImageAnnotate", "Ctrl+E",
                    "annotate"),
                new("builtin.pinned.hide-mark", "BuiltinShortcutPinnedContext", "PinnedMenuHideMark", "H", "hide-mark"),
                new("builtin.pinned.copy-plain-text", "BuiltinShortcutPinnedContext", "PinnedCopyPlainText",
                    "Ctrl+Shift+C",
                    "copy-plain-text"),
                new("builtin.pinned.copy-html", "BuiltinShortcutPinnedContext", "PinnedCopyWithHtml", "Ctrl+Alt+C",
                    "copy-html"),
                new("builtin.pinned.destroy", "BuiltinShortcutPinnedContext", "PinnedImageDestroy", "Ctrl+D",
                    "destroy"),
                new("builtin.pinned.destroy-alternate", "BuiltinShortcutPinnedContext", "PinnedImageDestroy",
                    "Shift+Escape", "destroy"),
                new("builtin.pinned.copy-file", "BuiltinShortcutPinnedContext", "PinnedImageCopyFile", "", "copy-file"),
                new("builtin.pinned.annotate-toolbar", "BuiltinShortcutPinnedContext", "PinnedImageAnnotationToolbar",
                    "Space", "annotate-toolbar"),
                new("builtin.pinned.thumbnail", "BuiltinShortcutPinnedContext", "PinnedImageThumbnail", "R",
                    "thumbnail"),
                new("builtin.pinned.select-roi", "BuiltinShortcutPinnedContext", "PinnedImageSelectThumbnailRegion", "",
                    "select-roi"),
                new("builtin.pinned.restore-roi", "BuiltinShortcutPinnedContext", "PinnedImageRestoreRegion", "",
                    "restore-roi"),
                new("builtin.pinned.reset", "BuiltinShortcutPinnedContext", "MouseActionReset", "", "reset"),
                new("builtin.pinned.move-up", "BuiltinShortcutPinnedContext", "MouseActionMoveWindow", "Up", "move-up",
                    DirectionKey: Key.Up),
                new("builtin.pinned.move-down", "BuiltinShortcutPinnedContext", "MouseActionMoveWindow", "Down",
                    "move-down", DirectionKey: Key.Down),
                new("builtin.pinned.move-left", "BuiltinShortcutPinnedContext", "MouseActionMoveWindow", "Left",
                    "move-left", DirectionKey: Key.Left),
                new("builtin.pinned.move-right", "BuiltinShortcutPinnedContext", "MouseActionMoveWindow", "Right",
                    "move-right", DirectionKey: Key.Right),
                new("builtin.pinned.select", "BuiltinShortcutPinnedContext", "PinnedImageMultiSelect", "", "select"),
                new("builtin.pinned.rotate-counterclockwise", BuiltinShortcutContexts.PinnedImage,
                    "PinnedImageRotateCounterclockwise", "2",
                    "rotate-counterclockwise"),
                new("builtin.pinned.rotate-clockwise", BuiltinShortcutContexts.PinnedImage,
                    "PinnedImageRotateClockwise", "1",
                    "rotate-clockwise"),
                new("builtin.pinned.flip-horizontal", BuiltinShortcutContexts.PinnedImage, "AnimationFlipHorizontal",
                    "3",
                    "flip-horizontal"),
                new("builtin.pinned.flip-vertical", BuiltinShortcutContexts.PinnedImage, "AnimationFlipVertical", "4",
                    "flip-vertical"),
                new("builtin.pinned.grayscale", BuiltinShortcutContexts.PinnedImage, "AnimationGrayscale", "5",
                    "grayscale"),
                new("builtin.pinned.invert", BuiltinShortcutContexts.PinnedImage, "AnimationInvert", "6", "invert"),
                new("builtin.pinned.menu", "BuiltinShortcutPinnedContext", "MouseActionMenu", "Apps", "menu"),
                new("builtin.pinned.menu-alternate", "BuiltinShortcutPinnedContext", "MouseActionMenu", "Shift+F10",
                    "menu"),
                new("builtin.longshot.crop-leading", "BuiltinShortcutLongshotContext", "ScrollCaptureCropTop", "Alt+2",
                    "crop-leading"),
                new("builtin.longshot.crop-trailing", "BuiltinShortcutLongshotContext", "ScrollCaptureCropBottom",
                    "Alt+3", "crop-trailing"),
                new("builtin.longshot.edit", "BuiltinShortcutLongshotContext", "ScrollCaptureEdit", "Alt+5", "edit"),
            ];
        }

        private static ReadOnlyCollection<BuiltinShortcutBinding> BuildExternal(Dictionary<string, string[]>? settings,
            out List<SelectionShortcutIssue> issues, bool mixing = false,
            List<BuiltinShortcutDefaultOverride>? overrides = null)
        {
            issues = [];
            if (settings is not null)
            {
                var ids = Catalog.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
                // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                foreach (var id in settings.Keys)
                    if (!ids.Contains(id))
                        issues.Add(new(id, null, UiText.Get("SelectionShortcutInvalid") + ": " + id));
            }

            foreach (var definition in Registrations())
            {
                if (!ReadSetting(settings, definition.Id, out var configured)) continue;
                if (configured is null || configured.Length > 1 || configured.Any(string.IsNullOrWhiteSpace))
                    issues.Add(new(definition.Id, null, UiText.Get("SelectionShortcutInvalid") + ": " +
                                                        UiText.Get(definition.DescriptionKey)));
            }

            var result = new List<BuiltinShortcutBinding>();
            var used = new Dictionary<(Key Key, KeyModifiers Modifiers), List<BuiltinShortcutDefinition>>();
            foreach (var definition in Catalog.Where(definition =>
                         definition.SelectionId is null || definition.ContextKey == BuiltinShortcutContexts.CapturePin))
            {
                var value = GetGesture(definition, settings);
                if (value.Length == 0) continue;
                KeyGesture gesture;
                try
                {
                    gesture = KeyGestureDisplay.Parse(value);
                    if (!Enum.IsDefined(gesture.Key) || gesture.Key is Key.None or Key.LeftCtrl or Key.RightCtrl
                            or Key.LeftShift
                            or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin ||
                        (gesture.KeyModifiers & ~(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt |
                                                  KeyModifiers.Meta)) != 0)
                        throw new FormatException();
                }
                catch (Exception error) when (error is ArgumentException or FormatException)
                {
                    issues.Add(new(definition.Id, null,
                        UiText.Get("SelectionShortcutInvalid") + ": " + UiText.Get(definition.DescriptionKey)));
                    continue;
                }

                var context = definition.ContextKey == BuiltinShortcutContexts.CapturePin
                    ? BuiltinShortcutContexts.Pinned
                    : definition.ContextKey;
                var key = BuiltinKeys.Normalize(gesture.Key, mixing);
                if (!used.TryGetValue((key, gesture.KeyModifiers), out var matches))
                    used.Add((key, gesture.KeyModifiers), matches = []);
                var conflict = false;
                foreach (var existing in matches.Where(existing =>
                             BuiltinShortcutContexts.Overlap(
                                 existing.ContextKey == BuiltinShortcutContexts.CapturePin
                                     ? BuiltinShortcutContexts.Pinned
                                     : existing.ContextKey, context)))
                {
                    if (NativeDefaultShadow(existing, definition, key, gesture.KeyModifiers, mixing))
                    {
                        overrides?.Add(existing.ContextKey == BuiltinShortcutContexts.Pinned
                            ? new(existing, definition)
                            : new(definition, existing));
                        continue;
                    }

                    issues.Add(new(definition.Id, existing.Id, UiText.Get("SelectionShortcutConflict") + ": " +
                                                               UiText.Get(existing.DescriptionKey) + " / " +
                                                               UiText.Get(definition.DescriptionKey)));
                    conflict = true;
                }

                if (conflict) continue;
                matches.Add(definition);
                var action = definition.ContextKey == BuiltinShortcutContexts.CapturePin
                    ? SelectionCommands.PinnedAction(Enum.Parse<SelectionCommand>(definition.Action, true))!
                    : definition.Action;
                result.Add(new(context, action, gesture) { DefinitionId = definition.Id });
            }

            return result.AsReadOnly();
        }

        private static bool NativeDefaultShadow(BuiltinShortcutDefinition first, BuiltinShortcutDefinition second,
            Key key, KeyModifiers modifiers, bool mixing)
        {
            if (first.ContextKey == second.ContextKey ||
                (first.ContextKey != BuiltinShortcutContexts.Pinned &&
                 second.ContextKey != BuiltinShortcutContexts.Pinned) ||
                first.DefaultGesture.Length == 0 || second.DefaultGesture.Length == 0) return false;
            var firstDefault = KeyGestureDisplay.Parse(first.DefaultGesture);
            var secondDefault = KeyGestureDisplay.Parse(second.DefaultGesture);
            return BuiltinKeys.Normalize(firstDefault.Key, mixing) == key && firstDefault.KeyModifiers == modifiers &&
                   BuiltinKeys.Normalize(secondDefault.Key, mixing) == key && secondDefault.KeyModifiers == modifiers;
        }
    }
}
