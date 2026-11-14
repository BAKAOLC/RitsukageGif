using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class BuiltinShortcuts
    {
        private static bool IsPinnedAnnotationAction(string action)
        {
            return action is "window-shade" or "configuration" or "paste" or "set-title" or "hide-others"
                or "switch-next" or "new-group" or "wheel-up" or "wheel-down" or "print" or "close"
                or "annotate-toolbar" or "copy-all-text" or "annotate" or "destroy";
        }

        internal static string? ResolvePinnedAnnotation(IReadOnlyList<BuiltinShortcutBinding> bindings,
            KeyEventArgs args)
        {
            if (args.Handled || BuiltinShortcutTextInput.OwnsKey(args)) return null;
            foreach (var binding in bindings)
                if (binding.ContextKey == BuiltinShortcutContexts.Pinned &&
                    binding.DefinitionId != "builtin.selection.cancel.0" &&
                    IsPinnedAnnotationAction(binding.Action) &&
                    BuiltinKeys.Matches(binding.Gesture.Key, args.Key) &&
                    binding.Gesture.KeyModifiers == args.KeyModifiers)
                    return binding.Action;
            return null;
        }

        internal static IReadOnlyList<SelectionShortcutIssue> ValidateAnnotationBindings(
            IReadOnlyList<SelectionCommandDefinition> selection, Dictionary<string, string[]>? settings,
            bool mixing, List<BuiltinShortcutDefaultOverride>? overrides = null)
        {
            var issues = new List<SelectionShortcutIssue>();
            var external = BuildExternal(settings, out var externalIssues, mixing);
            if (externalIssues.Count > 0) return issues;
            foreach (var binding in selection.Where(binding =>
                         SelectionCommands.SupportsPinnedAnnotation(binding.Command)))
            {
                var key = BuiltinKeys.Normalize(binding.Key, mixing);
                var registrations = Catalog.Where(definition => definition.SelectionId == binding.Id).ToArray();
                var registration = registrations.FirstOrDefault(definition =>
                {
                    if (definition.DefaultGesture.Length == 0) return false;
                    var gesture = KeyGestureDisplay.Parse(definition.DefaultGesture);
                    return BuiltinKeys.Normalize(gesture.Key, mixing) == key &&
                           gesture.KeyModifiers == binding.Modifiers;
                }) ?? registrations[0];
                foreach (var pin in external.Where(item => item.ContextKey == BuiltinShortcutContexts.Pinned &&
                                                           !item.DefinitionId.StartsWith("builtin.selection.",
                                                               StringComparison.Ordinal) &&
                                                           IsPinnedAnnotationAction(item.Action) &&
                                                           BuiltinKeys.Normalize(item.Gesture.Key, mixing) == key &&
                                                           item.Gesture.KeyModifiers == binding.Modifiers))
                {
                    var parent = Catalog.First(definition =>
                    {
                        if (definition.SelectionId is not null || definition.ContextKey != pin.ContextKey ||
                            definition.Action != pin.Action) return false;
                        var value = GetGesture(definition, settings);
                        if (value.Length == 0) return false;
                        var gesture = KeyGestureDisplay.Parse(value);
                        return BuiltinKeys.Normalize(gesture.Key, mixing) == key &&
                               gesture.KeyModifiers == binding.Modifiers;
                    });
                    if (NativeDefaultShadow(parent, registration, key, binding.Modifiers, mixing))
                    {
                        if (binding.Command == SelectionCommand.TogglePolylineArrows)
                            overrides?.Add(new(parent, registration));
                        continue;
                    }

                    issues.Add(new(registration.Id, parent.Id, UiText.Get("SelectionShortcutConflict") + ": " +
                                                               UiText.Get(registration.DescriptionKey) + " / " +
                                                               UiText.Get(parent.DescriptionKey)));
                }
            }

            return issues;
        }
    }
}
