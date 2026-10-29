using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private bool _mouseInputHeld;
        private bool AllInputReleased => !_left && !_right && !_enter && !_escape && !_mouseInputHeld;

        internal IReadOnlyList<MouseCommandBinding> MouseBindings { get; } = MouseCommands.SnapshotBindings();

        internal MouseGestureContext MouseContext =>
            ScreenshotMode ? MouseGestureContext.Screenshot : MouseGestureContext.Selection;

        internal void UpdateAnnotationPointer(PixelPoint point)
        {
            if (_finished || !ScreenshotMode || (!ScreenshotDrawing && Annotations is not { IsBusy: true }) ||
                _pointer == point) return;
            _pointer = point;
            _mouse = point;
            if (Controls && !_controlPending &&
                Annotations is
                    { Tool: ScreenshotTool.Rectangle or ScreenshotTool.Ellipse, IsDrawing: false, IsBusy: false } &&
                CurrentControlTarget is { } target)
                _ = DetectControlAsync(target, point);
            Refresh();
        }

        internal void SetMouseInputHeld(bool held)
        {
            _mouseInputHeld = held;
            if (held) CancelModifierTap();
            if (!_finished && _pendingFinish && AllInputReleased) CompleteSelection(_pendingResult);
            if (!held) ResumeAutoConfirm();
        }

        internal void HoverInput(PixelPoint point, bool shift)
        {
            if (_finished || _pendingFinish || _screenshotCompleting || ScreenshotDrawing ||
                Annotations is { IsBusy: true }) return;
            try
            {
                _pointer = point;
                UpdateShift(shift);
                UpdatePoint();
                Refresh();
            }
            catch (Exception error)
            {
                Finish(null, error);
            }
        }

        internal void ExecuteMouseAction(MouseAction action, PixelPoint point)
        {
            if (_finished) return;
            try
            {
                _pointer = point;
                UpdatePoint();
                var command = action switch
                {
                    MouseAction.Confirm => SelectionCommand.Confirm,
                    MouseAction.Cancel => SelectionCommand.Cancel,
                    MouseAction.Clear => SelectionCommand.Clear,
                    MouseAction.Copy => SelectionCommand.Copy,
                    MouseAction.Save => SelectionCommand.Save,
                    MouseAction.ToggleCursor => SelectionCommand.ToggleCursor,
                    MouseAction.ToggleControls => SelectionCommand.ToggleControls,
                    MouseAction.ToggleSource => SelectionCommand.ToggleSource,
                    MouseAction.CycleHints => SelectionCommand.CycleHints,
                    MouseAction.CycleSnapping => SelectionCommand.CycleSnapping,
                    MouseAction.Undo => SelectionCommand.Undo,
                    MouseAction.Redo => SelectionCommand.Redo,
                    _ => (SelectionCommand?)null,
                };
                if (command is null) return;
                ExecuteSelectionCommand(new(command.Value, Key.None, KeyModifiers.None, string.Empty));
                Refresh();
            }
            catch (Exception error)
            {
                Finish(null, error);
            }
        }

        internal void CancelMouseDrag()
        {
            if (!CancelSelectionManipulation() && _state.Dragging) ClearSelection(false);
            _left = _right = false;
            _rightConfirm = false;
            Refresh();
        }
    }
}
