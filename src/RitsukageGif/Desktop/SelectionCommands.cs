using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class SelectionCommands
    {
        private static readonly SelectionCommandDefinition[] DefaultDefinitions =
        [
            new(SelectionCommand.SelectScreenOrAll, Key.A, KeyModifiers.Control, "SelectionScreenOrAll"),
            new(SelectionCommand.PreviousRegion, Key.R, KeyModifiers.None, "SelectionPreviousRegion"),
            new(SelectionCommand.NextRegion, Key.R, KeyModifiers.Shift, "SelectionNextRegion"),
            new(SelectionCommand.PreviousScreenshot, Key.OemComma, KeyModifiers.None, "SelectionPreviousScreenshot"),
            new(SelectionCommand.NextScreenshot, Key.OemPeriod, KeyModifiers.None, "SelectionNextScreenshot"),
            new(SelectionCommand.RefreshScreenshot, Key.F5, KeyModifiers.None, "SelectionRefreshScreenshot"),
            new(SelectionCommand.MovePointer, Key.W, KeyModifiers.None, "SelectionMovePointer", true,
                DirectionKey: Key.Up),
            new(SelectionCommand.MovePointer, Key.S, KeyModifiers.None, "SelectionMovePointer", true,
                DirectionKey: Key.Down),
            new(SelectionCommand.MovePointer, Key.A, KeyModifiers.None, "SelectionMovePointer", true,
                DirectionKey: Key.Left),
            new(SelectionCommand.MovePointer, Key.D, KeyModifiers.None, "SelectionMovePointer", true,
                DirectionKey: Key.Right),
            new(SelectionCommand.Confirm, Key.Enter, KeyModifiers.None, "SelectionCommandConfirm"),
            new(SelectionCommand.Copy, Key.C, KeyModifiers.Control, "ScreenshotCopy"),
            new(SelectionCommand.Save, Key.S, KeyModifiers.Control, "ScreenshotSave"),
            new(SelectionCommand.Pin, Key.T, KeyModifiers.Control, "ScreenshotPin"),
            new(SelectionCommand.ScrollVertical, Key.L, KeyModifiers.None, "ScreenshotScrollVertical"),
            new(SelectionCommand.ScrollHorizontal, Key.None, KeyModifiers.None, "ScreenshotScrollHorizontal"),
            new(SelectionCommand.RecognizeText, Key.None, KeyModifiers.None, "RecognizeText"),
            new(SelectionCommand.RecognizeQr, Key.None, KeyModifiers.None, "RecognizeBarcode"),
            new(SelectionCommand.RecognizeTextAndCopy, Key.C, KeyModifiers.Shift, "RecognizeTextAndCopy"),
            new(SelectionCommand.CopyColor, Key.C, KeyModifiers.None, "SelectionCopyColor"),
            new(SelectionCommand.CycleColorFormat, Key.LeftShift, KeyModifiers.None, "SelectionColorFormat"),
            new(SelectionCommand.SelectTool, Key.V, KeyModifiers.None, "AnimationSelection",
                Tool: ScreenshotTool.Select),
            new(SelectionCommand.SelectTool, Key.None, KeyModifiers.None, "AnimationEllipse",
                Tool: ScreenshotTool.Ellipse),
            new(SelectionCommand.SelectTool, Key.D1, KeyModifiers.Shift, "AnimationRectangle",
                Tool: ScreenshotTool.Rectangle),
            new(SelectionCommand.SelectTool, Key.D3, KeyModifiers.Shift, "ScreenshotArrow", Tool: ScreenshotTool.Arrow),
            new(SelectionCommand.SelectTool, Key.D5, KeyModifiers.Shift, "AnimationBrush", Tool: ScreenshotTool.Brush),
            new(SelectionCommand.SelectTool, Key.D6, KeyModifiers.Shift, "ScreenshotHighlight",
                Tool: ScreenshotTool.Highlight),
            new(SelectionCommand.SelectTool, Key.D8, KeyModifiers.Shift, "AnimationText", Tool: ScreenshotTool.Text),
            new(SelectionCommand.SelectTool, Key.D7, KeyModifiers.Shift, "AnimationMosaic",
                Tool: ScreenshotTool.Mosaic),
            new(SelectionCommand.SelectTool, Key.None, KeyModifiers.None, "AnimationBlur",
                Tool: ScreenshotTool.Blur),
            new(SelectionCommand.SelectTool, Key.None, KeyModifiers.None, "AnnotationToolLine",
                Tool: ScreenshotTool.Line),
            new(SelectionCommand.SelectTool, Key.D2, KeyModifiers.Shift, "AnnotationToolPolyline",
                Tool: ScreenshotTool.Polyline),
            new(SelectionCommand.SelectTool, Key.D4, KeyModifiers.Shift, "AnnotationToolNumber",
                Tool: ScreenshotTool.Number),
            new(SelectionCommand.SelectTool, Key.D9, KeyModifiers.Shift, "AnnotationToolEraser",
                Tool: ScreenshotTool.Eraser),
            new(SelectionCommand.SelectTool, Key.None, KeyModifiers.None, "AnnotationToolStrokeEraser",
                Tool: ScreenshotTool.StrokeEraser),
            new(SelectionCommand.SelectTool, Key.D0, KeyModifiers.Shift, "AnnotationToolSpotlight",
                Tool: ScreenshotTool.Spotlight),
            new(SelectionCommand.SelectTool, Key.OemMinus, KeyModifiers.Shift, "AnnotationToolWatermark",
                Tool: ScreenshotTool.Watermark),
            new(SelectionCommand.SelectTool, Key.OemPlus, KeyModifiers.Shift, "AnnotationToolMagnifier",
                Tool: ScreenshotTool.Magnifier),
            new(SelectionCommand.ToggleAnnotations, Key.None, KeyModifiers.None, "AnnotationToggleVisibility"),
            new(SelectionCommand.ClearAnnotations, Key.Z, KeyModifiers.Control | KeyModifiers.Shift,
                "AnnotationClearAll"),
            new(SelectionCommand.RenumberAnnotations, Key.None, KeyModifiers.None, "AnnotationRenumber"),
            new(SelectionCommand.FinishPolyline, Key.None, KeyModifiers.None, "AnnotationFinishPolyline"),
            new(SelectionCommand.Clear, Key.Delete, KeyModifiers.None, "SelectionCommandClear"),
            new(SelectionCommand.Clear, Key.Back, KeyModifiers.None, "SelectionCommandClear"),
            new(SelectionCommand.Undo, Key.Z, KeyModifiers.Control, "AnimationUndo"),
            new(SelectionCommand.Redo, Key.Y, KeyModifiers.Control, "AnimationRedo"),
            new(SelectionCommand.Cancel, Key.Escape, KeyModifiers.None, "SelectionCommandCancel"),
            new(SelectionCommand.ToggleControls, Key.Tab, KeyModifiers.None, "SelectionCommandControls"),
            new(SelectionCommand.ToggleSource, Key.None, KeyModifiers.None, "SelectionCommandSource"),
            new(SelectionCommand.ToggleCursor, Key.OemTilde, KeyModifiers.None, "SelectionCommandCursor"),
            new(SelectionCommand.CycleHints, Key.F1, KeyModifiers.None, "SelectionCommandHints"),
            new(SelectionCommand.CycleSnapping, Key.Tab, KeyModifiers.Shift, "SelectionCommandSnapping"),
            new(SelectionCommand.Move, Key.Left, KeyModifiers.None, "SelectionCommandMove", true,
                DirectionKey: Key.Left),
            new(SelectionCommand.Move, Key.Right, KeyModifiers.None, "SelectionCommandMove", true,
                DirectionKey: Key.Right),
            new(SelectionCommand.Move, Key.Up, KeyModifiers.None, "SelectionCommandMove", true, DirectionKey: Key.Up),
            new(SelectionCommand.Move, Key.Down, KeyModifiers.None, "SelectionCommandMove", true,
                DirectionKey: Key.Down),
            new(SelectionCommand.Expand, Key.Left, KeyModifiers.Control, "SelectionCommandExpand", true,
                DirectionKey: Key.Left),
            new(SelectionCommand.Expand, Key.Right, KeyModifiers.Control, "SelectionCommandExpand", true,
                DirectionKey: Key.Right),
            new(SelectionCommand.Expand, Key.Up, KeyModifiers.Control, "SelectionCommandExpand", true,
                DirectionKey: Key.Up),
            new(SelectionCommand.Expand, Key.Down, KeyModifiers.Control, "SelectionCommandExpand", true,
                DirectionKey: Key.Down),
            new(SelectionCommand.Shrink, Key.Left, KeyModifiers.Shift, "SelectionCommandShrink", true,
                DirectionKey: Key.Left),
            new(SelectionCommand.Shrink, Key.Right, KeyModifiers.Shift, "SelectionCommandShrink", true,
                DirectionKey: Key.Right),
            new(SelectionCommand.Shrink, Key.Up, KeyModifiers.Shift, "SelectionCommandShrink", true,
                DirectionKey: Key.Up),
            new(SelectionCommand.Shrink, Key.Down, KeyModifiers.Shift, "SelectionCommandShrink", true,
                DirectionKey: Key.Down),
        ];

        private static SelectionCommandDefinition[] _definitions =
            [.. DefaultDefinitions.Where(item => item.Key != Key.None)];

        internal static bool TryResolve(Key key, KeyModifiers modifiers, out SelectionCommandDefinition? definition,
            IReadOnlyList<SelectionCommandDefinition>? bindings = null)
        {
            foreach (var item in bindings ?? _definitions)
            {
                if (item.Modifiers != modifiers || item.Key != key) continue;
                definition = item;
                return true;
            }

            definition = null;
            return false;
        }

        internal static bool IsCommandKey(Key key, KeyModifiers modifiers,
            IReadOnlyList<SelectionCommandDefinition>? bindings = null)
        {
            return TryResolve(key, modifiers, out _, bindings) || key is Key.LeftShift or Key.RightShift;
        }

        internal static bool IsAvailable(SelectionCommand command, SelectionSnapshot snapshot,
            bool controls, bool source, bool cursor, bool drawing = false, bool screenshot = false,
            bool editing = false)
        {
            if (snapshot.Phase == SelectionPhase.Confirming) return command == SelectionCommand.Cancel;
            var adjusting = snapshot is { Phase: SelectionPhase.Selected, Region.IsEmpty: false } && !drawing &&
                            !editing;
            var idle = snapshot.Phase is SelectionPhase.Candidate or SelectionPhase.Selected && !drawing && !editing;
            return command switch
            {
                SelectionCommand.SelectScreenOrAll or SelectionCommand.PreviousRegion
                    or SelectionCommand.NextRegion => idle,
                SelectionCommand.PreviousScreenshot or SelectionCommand.NextScreenshot
                    or SelectionCommand.RefreshScreenshot => screenshot && !drawing,
                SelectionCommand.MovePointer => !drawing && !editing,
                SelectionCommand.ToggleControls => controls && idle,
                SelectionCommand.ToggleSource => source && idle,
                SelectionCommand.ToggleCursor => cursor && !drawing,
                SelectionCommand.CycleHints => true,
                SelectionCommand.CycleSnapping => !drawing,
                SelectionCommand.Move or SelectionCommand.Expand or SelectionCommand.Shrink => adjusting,
                SelectionCommand.Confirm => snapshot.CanConfirm && !drawing,
                SelectionCommand.Copy or SelectionCommand.Save or SelectionCommand.Pin or SelectionCommand.RecognizeText
                    or SelectionCommand.RecognizeQr or SelectionCommand.RecognizeTextAndCopy
                    or SelectionCommand.ScrollVertical or SelectionCommand.ScrollHorizontal =>
                    screenshot && snapshot.CanConfirm && !drawing,
                SelectionCommand.CopyColor or SelectionCommand.CycleColorFormat => !drawing && !editing,
                SelectionCommand.Undo or SelectionCommand.Redo => screenshot,
                SelectionCommand.ToggleAnnotations or SelectionCommand.ClearAnnotations
                    or SelectionCommand.RenumberAnnotations => screenshot && !drawing,
                SelectionCommand.FinishPolyline => screenshot,
                SelectionCommand.SelectTool => screenshot && snapshot.Phase == SelectionPhase.Selected && !drawing,
                SelectionCommand.Clear => snapshot.Phase is not SelectionPhase.Candidate,
                SelectionCommand.Cancel => true,
                _ => false,
            };
        }

        internal static PixelRect AdjustRegion(PixelRect region, PixelRect limits, SelectionCommand command, Key key)
        {
            if (region.IsEmpty || limits.IsEmpty || !IsDirection(key)) return region;
            var width = Math.Min(region.Width, limits.Width);
            var height = Math.Min(region.Height, limits.Height);
            var x = Math.Clamp(region.X, limits.X, limits.Right - width);
            var y = Math.Clamp(region.Y, limits.Y, limits.Bottom - height);
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (command)
            {
                case SelectionCommand.Move:
                    var dx = key switch { Key.Left => -1, Key.Right => 1, _ => 0 };
                    var dy = key switch { Key.Up => -1, Key.Down => 1, _ => 0 };
                    x = (int)Math.Clamp((long)x + dx, limits.X, (long)limits.Right - width);
                    y = (int)Math.Clamp((long)y + dy, limits.Y, (long)limits.Bottom - height);
                    break;
                case SelectionCommand.Expand:
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (key)
                    {
                        case Key.Left when x > limits.X:
                            x--;
                            width++;
                            break;
                        case Key.Right when (long)x + width < limits.Right: width++; break;
                        case Key.Up when y > limits.Y:
                            y--;
                            height++;
                            break;
                        case Key.Down when (long)y + height < limits.Bottom: height++; break;
                    }

                    break;
                case SelectionCommand.Shrink:
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (key)
                    {
                        case Key.Left when width > 1:
                            x++;
                            width--;
                            break;
                        case Key.Right when width > 1: width--; break;
                        case Key.Up when height > 1:
                            y++;
                            height--;
                            break;
                        case Key.Down when height > 1: height--; break;
                    }

                    break;
                default:
                    return region;
            }

            return new(x, y, width, height);
        }

        private static bool IsDirection(Key key)
        {
            return key is Key.Left or Key.Right or Key.Up or Key.Down;
        }
    }
}
