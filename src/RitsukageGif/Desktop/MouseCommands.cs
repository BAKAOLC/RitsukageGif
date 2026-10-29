using System.Collections.ObjectModel;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static class MouseCommands
    {
        private static IReadOnlyList<MouseCommandBinding> _bindings;

        static MouseCommands()
        {
            _bindings = BuildDefaults();
        }

        private static IReadOnlyList<MouseCommandDefinition> Definitions { get; } = BuildCatalog();

        internal static IReadOnlyList<MouseCommandDefinition> Catalog { get; } =
            [.. Definitions.Where(item => item.Context == MouseGestureContext.Pinned)];

        internal static IReadOnlyList<MouseCommandBinding> SnapshotBindings()
        {
            return _bindings;
        }

        internal static bool TryResolve(MouseGestureContext context, InputMouseGesture gesture,
            IReadOnlyList<MouseCommandBinding> bindings, out MouseCommandBinding? binding)
        {
            foreach (var candidate in bindings)
            {
                if (candidate.Definition.Context != context || candidate.Gesture != gesture) continue;
                binding = candidate;
                return true;
            }

            binding = null;
            return false;
        }

        internal static IReadOnlyList<MouseShortcutIssue> ValidateIssues(Dictionary<string, string[]>? overrides)
        {
            return BuildBindings(overrides, out _);
        }

        internal static IReadOnlyList<string> Validate(Dictionary<string, string[]>? overrides)
        {
            return [.. ValidateIssues(overrides).Select(issue => issue.Message)];
        }

        internal static IReadOnlyList<string> Configure(Dictionary<string, string[]>? overrides)
        {
            var errors = BuildBindings(overrides, out var bindings);
            if (errors.Count == 0) _bindings = bindings;
            return [.. errors.Select(issue => issue.Message)];
        }

        private static IReadOnlyList<MouseCommandBinding> BuildDefaults()
        {
            var errors = BuildBindings(null, out var bindings);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors));
            return bindings;
        }

        private static List<MouseShortcutIssue> BuildBindings(Dictionary<string, string[]>? overrides,
            out IReadOnlyList<MouseCommandBinding> bindings)
        {
            var errors = new List<MouseShortcutIssue>();
            var result = new List<MouseCommandBinding>();
            var used = new Dictionary<(MouseGestureContext, InputMouseGesture), MouseCommandDefinition>();
            foreach (var definition in Definitions)
            {
                var gestures = definition.Context == MouseGestureContext.Pinned && overrides is not null &&
                               overrides.TryGetValue(definition.Id, out var configured)
                    ? configured
                    : definition.DefaultGestures;
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (gestures is null)
                {
                    errors.Add(new(definition.Id, null,
                        UiText.Get("MouseShortcutInvalid") + ": " + UiText.Get(definition.DescriptionKey)));
                    continue;
                }

                if (definition.Context == MouseGestureContext.Pinned && gestures.Count > 1)
                {
                    errors.Add(new(definition.Id, null,
                        UiText.Get("MouseShortcutInvalid") + ": " + UiText.Get(definition.DescriptionKey)));
                    continue;
                }

                foreach (var value in gestures)
                {
                    if (!InputMouseGesture.TryParse(value, out var gesture) ||
                        (definition.AllowedKinds & gesture.Kind) == 0)
                    {
                        errors.Add(new(definition.Id, null,
                            UiText.Get("MouseShortcutInvalid") + ": " + UiText.Get(definition.DescriptionKey) + " — " +
                            value));
                        continue;
                    }

                    if (definition.Context == MouseGestureContext.Pinned && gesture is
                            { Kind: MouseGestureKind.LeftClick, Modifiers: KeyModifiers.None })
                    {
                        errors.Add(new(definition.Id, null, UiText.Get("MouseLeftClickReserved")));
                        continue;
                    }

                    var key = (definition.Context, gesture);
                    if (used.TryGetValue(key, out var existing))
                    {
                        if (existing.Id != definition.Id)
                            errors.Add(new(definition.Id, existing.Id, UiText.Get("MouseShortcutConflict") + ": " +
                                                                       value + " — " +
                                                                       UiText.Get(existing.DescriptionKey) + ", " +
                                                                       UiText.Get(definition.DescriptionKey)));
                        continue;
                    }

                    used.Add(key, definition);
                    result.Add(new(definition, gesture));
                }
            }

            bindings = result.AsReadOnly();
            return errors;
        }

        private static ReadOnlyCollection<MouseCommandDefinition> BuildCatalog()
        {
            var result = new List<MouseCommandDefinition>();
            foreach (var context in new[]
                     {
                         MouseGestureContext.Selection, MouseGestureContext.Screenshot, MouseGestureContext.Annotation,
                     })
            {
                Add(context, "select", MouseAction.Select, "SelectionCommandMove",
                    MouseGestureKind.Clicks | MouseGestureKind.Drags,
                    "LeftClick", "Shift+LeftClick", "LeftDrag", "Shift+LeftDrag");
                if (context == MouseGestureContext.Annotation)
                {
                    var select = result[^1];
                    result[^1] = select with
                    {
                        DefaultGestures = Array.AsReadOnly<string>([
                            .. select.DefaultGestures, "LeftDoubleClick", "Shift+LeftDoubleClick",
                        ]),
                    };
                    Add(context, "confirm", MouseAction.Confirm, "SelectionCommandConfirm", MouseGestureKind.Discrete);
                }
                else
                {
                    Add(context, "confirm", MouseAction.Confirm, "SelectionCommandConfirm", MouseGestureKind.Discrete,
                        "LeftDoubleClick");
                }

                Add(context, "cancel", MouseAction.Cancel, "SelectionCommandCancel", MouseGestureKind.Discrete,
                    "RightClick", "RightDoubleClick");
                Add(context, "clear", MouseAction.Clear, "SelectionCommandClear", MouseGestureKind.Discrete);
                Add(context, "cursor", MouseAction.ToggleCursor, "SelectionCommandCursor", MouseGestureKind.Discrete);
                Add(context, "controls", MouseAction.ToggleControls, "SelectionCommandControls",
                    MouseGestureKind.Discrete);
                Add(context, "source", MouseAction.ToggleSource, "SelectionCommandSource", MouseGestureKind.Discrete);
                Add(context, "hints", MouseAction.CycleHints, "SelectionCommandHints", MouseGestureKind.Discrete);
                Add(context, "snapping", MouseAction.CycleSnapping, "SelectionCommandSnapping",
                    MouseGestureKind.Discrete);
            }

            foreach (var context in new[] { MouseGestureContext.Screenshot, MouseGestureContext.Annotation })
            {
                Add(context, "copy", MouseAction.Copy, "ScreenshotCopy", MouseGestureKind.Discrete);
                Add(context, "save", MouseAction.Save, "ScreenshotSave", MouseGestureKind.Discrete);
                Add(context, "undo", MouseAction.Undo, "AnimationUndo", MouseGestureKind.Discrete);
                Add(context, "redo", MouseAction.Redo, "AnimationRedo", MouseGestureKind.Discrete);
            }

            Add(MouseGestureContext.Pinned, "zoom", MouseAction.Zoom, "MouseActionZoom", MouseGestureKind.Wheel,
                "Wheel");
            Add(MouseGestureContext.Pinned, "fine-zoom", MouseAction.FineZoom, "MouseActionFineZoom",
                MouseGestureKind.Wheel, "Alt+Wheel");
            Add(MouseGestureContext.Pinned, "opacity", MouseAction.AdjustOpacity, "MouseActionOpacity",
                MouseGestureKind.Wheel, "Ctrl+Wheel");
            Add(MouseGestureContext.Pinned, "close", MouseAction.Close, "Close", MouseGestureKind.Clicks,
                "LeftDoubleClick");
            Add(MouseGestureContext.Pinned, "copy-and-close", MouseAction.CopyAndClose, "MouseActionCopyAndClose",
                MouseGestureKind.Clicks);
            Add(MouseGestureContext.Pinned, "destroy", MouseAction.Destroy, "PinnedImageDestroy",
                MouseGestureKind.Clicks);
            Add(MouseGestureContext.Pinned, "reset", MouseAction.Reset, "MouseActionReset", MouseGestureKind.Clicks,
                "MiddleClick");
            Add(MouseGestureContext.Pinned, "select-thumbnail-region", MouseAction.SelectThumbnailRegion,
                "PinnedImageSelectThumbnailRegion", MouseGestureKind.Drags, "RightDrag");
            Add(MouseGestureContext.Pinned, "thumbnail", MouseAction.ToggleThumbnail, "PinnedImageThumbnail",
                MouseGestureKind.Clicks, "Shift+LeftDoubleClick");
            Add(MouseGestureContext.Pinned, "move-thumbnail-region", MouseAction.MoveThumbnailRegion,
                "PinnedImageMoveThumbnailRegion", MouseGestureKind.Drags, "Shift+LeftDrag");
            Add(MouseGestureContext.Pinned, "copy", MouseAction.Copy, "ScreenshotCopy", MouseGestureKind.Clicks);
            Add(MouseGestureContext.Pinned, "copy-text", MouseAction.CopyRecognizedText, "PinnedImageCopyText",
                MouseGestureKind.Clicks, "Shift+RightClick");
            Add(MouseGestureContext.Pinned, "menu", MouseAction.ContextMenu, "MouseActionMenu", MouseGestureKind.Clicks,
                "RightClick");
            Add(MouseGestureContext.Pinned, "annotation-toolbar", MouseAction.ToggleAnnotationToolbar,
                "PinnedImageAnnotationToolbar", MouseGestureKind.Clicks);
            Add(MouseGestureContext.Pinned, "move", MouseAction.MoveWindow, "MouseActionAdjustWindow",
                MouseGestureKind.Drags, "LeftDrag");
            Add(MouseGestureContext.Pinned, "move-ignoring-text", MouseAction.MoveWindowIgnoringText,
                "PinnedImageMoveIgnoringText", MouseGestureKind.Drags, "Alt+LeftDrag");
            Add(MouseGestureContext.Pinned, "drag-content", MouseAction.DragContent, "PinnedImageDragContent",
                MouseGestureKind.Drags, "Ctrl+LeftDrag");
            Add(MouseGestureContext.Pinned, "multi-select", MouseAction.ToggleSelection, "PinnedImageMultiSelect",
                MouseGestureKind.Clicks, "Ctrl+LeftClick");
            return result.AsReadOnly();

            void Add(MouseGestureContext context, string id, MouseAction action, string description,
                MouseGestureKind allowed, params string[] gestures)
            {
                result.Add(new(context.ToString().ToLowerInvariant() + "." + id, context, action, description, allowed,
                    Array.AsReadOnly(gestures)));
            }
        }
    }
}
