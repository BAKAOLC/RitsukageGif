using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

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

        internal static List<string> SelectionGestures(string id, IReadOnlyList<string> defaults,
            Dictionary<string, string[]>? settings)
        {
            var result = new List<string>();
            for (var index = 0; index < defaults.Count; index++)
                if (ReadSetting(settings, SelectionSlotId(id, index), out var slot))
                    result.AddRange(slot ?? []);
                else if (defaults[index].Length > 0) result.Add(defaults[index]);
            return result;
        }

        internal static IReadOnlyList<SelectionShortcutIssue> ValidateExternal(Dictionary<string, string[]>? settings)
        {
            BuildExternal(settings, out var issues);
            return issues;
        }

        internal static void ConfigureExternal(Dictionary<string, string[]>? settings)
        {
            var bindings = BuildExternal(settings, out var issues);
            if (issues.Count == 0) _bindings = bindings;
        }

        internal static IReadOnlyList<BuiltinShortcutBinding> Snapshot()
        {
            return _bindings;
        }

        internal static string? Resolve(IReadOnlyList<BuiltinShortcutBinding> bindings, string context,
            KeyEventArgs args)
        {
            if (args.Handled || args.Source is TextBox ||
                (args.Source is Control source && source.FindAncestorOfType<TextBox>() is not null)) return null;
            foreach (var binding in bindings)
                if (binding.ContextKey == context && binding.Gesture.Key == args.Key &&
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
                new("builtin.pinned.lock", "BuiltinShortcutPinnedContext", "PinnedImageLock", "L", "lock"),
                new("builtin.pinned.topmost", "BuiltinShortcutPinnedContext", "PinnedImageTopmost", "T", "topmost"),
                new("builtin.pinned.through", "BuiltinShortcutPinnedContext", "PinnedImageMouseThrough", "", "through"),
                new("builtin.longshot.pause", "BuiltinShortcutLongshotContext", "BuiltinShortcutTogglePause", "Alt+4",
                    "pause"),
                new("builtin.longshot.toggleaxis", "BuiltinShortcutLongshotContext", "BuiltinShortcutToggleAxis", "",
                    "toggleaxis"),
                new("builtin.longshot.automatic", "BuiltinShortcutLongshotContext", "ScreenshotAutoScrolling", "Alt+1",
                    "automatic"),
                new("builtin.pinned.hide", "BuiltinShortcutPinnedContext", "PinnedImageHide", "", "hide"),
                new("builtin.pinned.copy", "BuiltinShortcutPinnedContext", "ScreenshotCopy", "Ctrl+C", "copy"),
                new("builtin.pinned.save", "BuiltinShortcutPinnedContext", "ScreenshotSave", "Ctrl+S", "save"),
                new("builtin.pinned.annotate", "BuiltinShortcutPinnedContext", "PinnedImageAnnotate", "Ctrl+E",
                    "annotate"),
                new("builtin.pinned.close", "BuiltinShortcutPinnedContext", "Close", "Ctrl+W", "close"),
                new("builtin.pinned.close-alternate", "BuiltinShortcutPinnedContext", "Close", "Escape", "close"),
                new("builtin.pinned.destroy", "BuiltinShortcutPinnedContext", "PinnedImageDestroy", "Ctrl+D",
                    "destroy"),
                new("builtin.pinned.destroy-alternate", "BuiltinShortcutPinnedContext", "PinnedImageDestroy",
                    "Shift+Escape", "destroy"),
                new("builtin.pinned.copy-all-text", "BuiltinShortcutPinnedContext", "PinnedImageCopyText", "Shift+C",
                    "copy-all-text"),
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
                new("builtin.pinned.rotate-clockwise", "BuiltinShortcutPinnedContext", "PinnedImageRotateClockwise", "",
                    "rotate-clockwise"),
                new("builtin.pinned.rotate180", "BuiltinShortcutPinnedContext", "PinnedImageRotate180", "",
                    "rotate180"),
                new("builtin.pinned.flip-horizontal", "BuiltinShortcutPinnedContext", "AnimationFlipHorizontal", "",
                    "flip-horizontal"),
                new("builtin.pinned.flip-vertical", "BuiltinShortcutPinnedContext", "AnimationFlipVertical", "",
                    "flip-vertical"),
                new("builtin.pinned.grayscale", "BuiltinShortcutPinnedContext", "AnimationGrayscale", "", "grayscale"),
                new("builtin.pinned.invert", "BuiltinShortcutPinnedContext", "AnimationInvert", "", "invert"),
                new("builtin.pinned.menu", "BuiltinShortcutPinnedContext", "MouseActionMenu", "Apps", "menu"),
                new("builtin.pinned.menu-alternate", "BuiltinShortcutPinnedContext", "MouseActionMenu", "Shift+F10",
                    "menu"),
                new("builtin.longshot.cancel", "BuiltinShortcutLongshotContext", "Cancel", "Escape", "cancel"),
                new("builtin.longshot.finish", "BuiltinShortcutLongshotContext", "ScreenshotCopy", "Enter",
                    "finish"),
                new("builtin.longshot.crop-leading", "BuiltinShortcutLongshotContext", "ScrollCaptureCropTop", "Alt+2",
                    "crop-leading"),
                new("builtin.longshot.crop-trailing", "BuiltinShortcutLongshotContext", "ScrollCaptureCropBottom",
                    "Alt+3", "crop-trailing"),
                new("builtin.longshot.edit", "BuiltinShortcutLongshotContext", "ScrollCaptureEdit", "Alt+5", "edit"),
            ];
        }

        private static ReadOnlyCollection<BuiltinShortcutBinding> BuildExternal(Dictionary<string, string[]>? settings,
            out List<SelectionShortcutIssue> issues)
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
            var used = new Dictionary<(string Context, Key Key, KeyModifiers Modifiers), BuiltinShortcutDefinition>();
            foreach (var definition in Catalog.Where(definition => definition.SelectionId is null))
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

                if (used.TryGetValue((definition.ContextKey, gesture.Key, gesture.KeyModifiers), out var existing))
                {
                    issues.Add(new(definition.Id, existing.Id, UiText.Get("SelectionShortcutConflict") + ": " +
                                                               UiText.Get(existing.DescriptionKey) + " / " +
                                                               UiText.Get(definition.DescriptionKey)));
                    continue;
                }

                used.Add((definition.ContextKey, gesture.Key, gesture.KeyModifiers), definition);
                result.Add(new(definition.ContextKey, definition.Action, gesture));
            }

            return result.AsReadOnly();
        }
    }
}
