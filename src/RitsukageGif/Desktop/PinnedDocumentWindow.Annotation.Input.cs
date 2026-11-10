using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private readonly HashSet<Key> _markTextKeys = [];

        private void ConfigureDocumentAnnotationInput()
        {
            AddHandler(KeyUpEvent, (_, args) =>
            {
                _markTextKeys.Remove(args.Key);
                _annotationScene?.ReleasePolylineArrowKey(args.Key);
                if (IsMarkEditing && _annotationScene?.HandleNumberCaptionKey(args.Key, args.KeyModifiers, false) ==
                    true)
                    args.Handled = true;
            }, RoutingStrategies.Tunnel, true);
            Deactivated += (_, _) =>
            {
                _annotationScene?.ReleaseNumberCaptionKey();
                _annotationScene?.ReleasePolylineArrowKey();
            };
        }

        private void DocumentAnnotationKeyDown(KeyEventArgs args)
        {
            if (_annotationScene is not { } session) return;
            if (args.Handled || _markTextKeys.Contains(args.Key) ||
                ((session.IsEditingText || FocusManager.GetFocusedElement() is TextBox) &&
                 BuiltinShortcutTextInput.IsEditingKey(args)))
            {
                _markTextKeys.Add(args.Key);
                return;
            }

            if (session.HandleNumberCaptionKey(args.Key, args.KeyModifiers, true))
            {
                args.Handled = true;
                return;
            }

            var hasAnnotationBinding = SelectionCommands.TryResolvePinnedAnnotation(args.Key, args.KeyModifiers,
                out var binding, session);
            if (hasAnnotationBinding && binding!.Command == SelectionCommand.TogglePolylineArrows)
            {
                args.Handled = session.HandlePolylineArrowKey(args.Key);
                return;
            }

            using var input = PinnedInputContext.Use(PinnedInputContext.Current | PinnedInputOrigin.Keyboard);
            var pin = BuiltinShortcuts.ResolvePinnedAnnotation(BuiltinShortcuts.Snapshot(), args);
            if (pin is not null && Presentation.Execute(pin))
            {
                args.Handled = true;
                return;
            }

            switch (pin)
            {
                case "annotate":
                    args.Handled = true;
                    EndDocumentMark();
                    return;
                case "close":
                    args.Handled = true;
                    CloseRequested?.Invoke(this, false);
                    return;
                case "destroy":
                    args.Handled = true;
                    CloseRequested?.Invoke(this, true);
                    return;
                case "wheel-up":
                    args.Handled = _simulatedWheel.Raise(1);
                    return;
                case "wheel-down":
                    args.Handled = _simulatedWheel.Raise(-1);
                    return;
                case "print":
                    args.Handled = true;
                    session.RequestComplete(ScreenshotOutputAction.Print);
                    return;
                case "annotate-toolbar":
                    args.Handled = true;
                    if (_annotationToolbar is { } toolbar)
                    {
                        if (toolbar.IsVisible)
                        {
                            toolbar.Hide();
                        }
                        else
                        {
                            toolbar.Show(this);
                            toolbar.Place();
                        }
                    }

                    return;
            }

            if (!hasAnnotationBinding) return;
            args.Handled = true;
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (binding!.Command)
            {
                case SelectionCommand.Cancel:
                    if (session.IsDrawing) session.CancelGesture();
                    else EndDocumentMark();
                    break;
                case SelectionCommand.Confirm:
                    if (!session.IsDrawing) EndDocumentMark();
                    break;
                case SelectionCommand.Undo: session.Undo(); break;
                case SelectionCommand.Redo: session.Redo(); break;
                case SelectionCommand.Clear: session.DeleteSelectedAnnotation(); break;
                case SelectionCommand.ClearAnnotations: session.ClearAnnotations(); break;
                case SelectionCommand.RenumberAnnotations: session.RenumberAnnotations(); break;
                case SelectionCommand.FinishPolyline: session.FinishPolyline(); break;
                case SelectionCommand.SelectTool:
                    session.SelectDrawingTool(binding.Tool ?? ScreenshotTool.Select); break;
                case SelectionCommand.Copy: session.RequestComplete(ScreenshotOutputAction.Copy); break;
                case SelectionCommand.Save: session.RequestComplete(ScreenshotOutputAction.Save); break;
                case SelectionCommand.QuickSave: session.RequestComplete(ScreenshotOutputAction.QuickSave); break;
                case SelectionCommand.RecognizeText: session.RequestRecognition(RecognitionMode.Text); break;
                case SelectionCommand.RecognizeQr: session.RequestRecognition(RecognitionMode.Barcode); break;
                case SelectionCommand.Translate: session.RequestTranslation(); break;
                case SelectionCommand.RecognizeTextAndCopy: _ = RunAsync(CopyDocumentRecognitionAsync); break;
                default: args.Handled = false; break;
            }
        }

        private async Task CopyDocumentRecognitionAsync()
        {
            var token = _lifetime.Token;
            var output = await ((IPinnedWindow)this).CaptureSceneOutputAsync(OutputColorMode.Sdr, token);
            var preferences = RecognitionPreferencesProvider();
            var result =
                await RecognitionOutput.RecognizeAsync(output.Png, preferences, TextRecognitionServiceProvider(),
                    token);
            var text = RecognitionTextFormatter.Format(result, preferences);
            if (text.Length == 0) throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
            await CopyTextAsync(text);
        }
    }
}
