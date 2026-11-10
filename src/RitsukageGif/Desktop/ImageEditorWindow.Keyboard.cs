using Avalonia.Controls;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ImageEditorWindow
    {
        private void EditorKeyDown(object? sender, KeyEventArgs args)
        {
            if (args.Handled || Session.IsEditingText ||
                FocusManager.GetFocusedElement() is TextBox or CompactNumberInput) return;
            if (Session.HandleNumberCaptionKey(args.Key, args.KeyModifiers, true))
            {
                args.Handled = true;
                return;
            }

            switch (args)
            {
                case { Key: Key.Escape, KeyModifiers: KeyModifiers.None } when IsStripEditing:
                    SetStripMode(null);
                    args.Handled = true;
                    return;
                case { Key: Key.Space, KeyModifiers: KeyModifiers.None }:
                    Session.Tool = Session.Tool == ScreenshotTool.Select ? _lastDrawingTool : ScreenshotTool.Select;
                    args.Handled = true;
                    return;
            }

            if (!SelectionCommands.TryResolve(args.Key, args.KeyModifiers, out var definition,
                    annotations: Session)) return;
            if (definition!.Command == SelectionCommand.TogglePolylineArrows)
            {
                args.Handled = Session.HandlePolylineArrowKey(args.Key);
                return;
            }

            args.Handled = ExecuteCommandCore(definition.Command, definition.Tool);
        }

        private void ExecuteCommand(SelectionCommand command)
        {
            ExecuteCommandCore(command, null);
        }

        private bool ExecuteCommandCore(SelectionCommand command, ScreenshotTool? tool)
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (command)
            {
                case SelectionCommand.Cancel:
                    if (Session.IsDrawing) Session.CancelGesture();
                    else Close();
                    break;
                case SelectionCommand.SelectTool: Session.SelectDrawingTool(tool ?? ScreenshotTool.Select); break;
                case SelectionCommand.SimulateWheelUp: _simulatedWheel.Raise(1); break;
                case SelectionCommand.SimulateWheelDown: _simulatedWheel.Raise(-1); break;
                case SelectionCommand.Undo: Session.Undo(); break;
                case SelectionCommand.Redo: Session.Redo(); break;
                case SelectionCommand.Clear: Session.DeleteSelectedAnnotation(); break;
                case SelectionCommand.ClearAnnotations: Session.ClearAnnotations(); break;
                case SelectionCommand.ToggleAnnotations: Session.ToggleAnnotations(); break;
                case SelectionCommand.ToggleSecondaryEditing: Session.ToggleSecondaryEditing(); break;
                case SelectionCommand.RenumberAnnotations: Session.RenumberAnnotations(); break;
                case SelectionCommand.FinishPolyline: Session.FinishPolyline(); break;
                case SelectionCommand.Copy: Session.RequestComplete(ScreenshotOutputAction.Copy); break;
                case SelectionCommand.Save: Session.RequestComplete(ScreenshotOutputAction.Save); break;
                case SelectionCommand.QuickSave: Session.RequestComplete(ScreenshotOutputAction.QuickSave); break;
                case SelectionCommand.Print when ScreenshotPrintService.IsSupported:
                    Session.RequestComplete(ScreenshotOutputAction.Print); break;
                case SelectionCommand.RecognizeText: Session.RequestRecognition(RecognitionMode.Text); break;
                case SelectionCommand.RecognizeTextAndCopy:
                    _ = RunAsync(async () =>
                    {
                        // ReSharper disable once InlineTemporaryVariable
                        if (RecognitionRequested is { } recognize)
                            await recognize(this, await ComposeSdrAsync(), RecognitionMode.Text, true, _lifetime.Token);
                    });
                    break;
                case SelectionCommand.RecognizeQr: Session.RequestRecognition(RecognitionMode.Barcode); break;
                case SelectionCommand.RecognizeTable: Session.RequestRecognition(RecognitionMode.Table); break;
                case SelectionCommand.RecognizeFormula: Session.RequestRecognition(RecognitionMode.Formula); break;
                case SelectionCommand.Translate: Session.RequestTranslation(); break;
                default: return false;
            }

            return true;
        }
    }
}
