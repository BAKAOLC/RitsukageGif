using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private readonly HashSet<Key> _annotationTextInputKeys = [];
        private ScreenshotAnnotationSession? _annotation;
        private CancellationTokenSource? _annotationCancellation;
        private ScreenshotAnnotationSession? _annotationScene;
        private ScreenshotAnnotationSurface? _annotationSurface;
        private PinnedAnnotationToolbar? _annotationToolbar;
        private PinnedImageState? _closedState;
        public Func<AnnotationPreferences> AnnotationPreferencesProvider { get; set; } = () => new();
        public Action<AnnotationPreferences>? AnnotationPreferencesChanged { get; set; }

        private void InitializeAnnotationKeyboard()
        {
            AddHandler(KeyDownEvent, (_, args) =>
            {
                if (IsAnnotationTextInputKey(args)) _annotationTextInputKeys.Add(args.Key);
            }, RoutingStrategies.Tunnel, true);
            AddHandler(KeyUpEvent, (_, args) =>
            {
                if (_annotationToolbar?.HandleAnnotationMenuKey(args, false) == true) return;
                _annotationTextInputKeys.Remove(args.Key);
                _annotation?.ReleasePolylineArrowKey(args.Key);
                if (_annotation?.HandleNumberCaptionKey(args.Key, args.KeyModifiers, false) == true)
                    args.Handled = true;
            }, RoutingStrategies.Tunnel, true);
            Deactivated += (_, _) =>
            {
                _annotation?.ReleaseNumberCaptionKey();
                _annotation?.ReleasePolylineArrowKey();
            };
        }

        private bool IsAnnotationTextInputKey(KeyEventArgs args)
        {
            return _annotationTextInputKeys.Contains(args.Key) ||
                   ((_annotation is { IsEditingText: true } || FocusManager.GetFocusedElement() is TextBox) &&
                    BuiltinShortcutTextInput.IsEditingKey(args));
        }

        private async Task BeginAnnotationAsync()
        {
            if (_annotation is not null || IsMouseThrough)
                return;
            try
            {
                await FreezeLiveAsync(_lifetime.Token);
                await FreezeAnimationFrameAsync(_lifetime.Token);
                await BeginAnnotationCore();
            }
            catch
            {
                EndAnnotation();
                throw;
            }
        }

        private Task BeginAnnotationCore()
        {
            if (IsMarkHidden) SetMarkHidden(false);
            if (_annotation is not null || IsMouseThrough) return Task.CompletedTask;
            ClearTextSelection();
            var session = _annotation = EnsureAnnotationScene();
            session.CanAnnotate = true;
            session.ApplyPinnedPreferences(AnnotationPreferencesProvider());
            UpdateSize();
            var toolbar = _annotationToolbar = new(this, session, AnnotationOutputShortcutBindings,
                () => _ = RunAsync(SaveAsync), EndAnnotation);
            toolbar.Show(this);
            toolbar.Place();
            return Task.CompletedTask;
        }

        private ScreenshotAnnotationSession EnsureAnnotationScene()
        {
            if (_annotationScene is { } existing) return existing;
            _annotationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var region = new PixelRect(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height);
            var session = _annotationScene = new()
            {
                CanScroll = false, CanAnnotate = false, Tool = ScreenshotTool.Brush,
                Underlay = HasTextMarkup ? _textMarkupSession : null,
            };
            session.ApplyPinnedPreferences(AnnotationPreferencesProvider());
            session.ToolbarPreferencesProvider = () => AnnotationPreferencesProvider().Toolbar;
            session.ToolbarPreferencesChanged += (preferences, tools) =>
            {
                var current = AnnotationPreferencesProvider();
                AnnotationPreferencesChanged?.Invoke(current with
                {
                    Toolbar = current.Toolbar.Merge(preferences, tools),
                });
            };
            session.PrivacyDetectionRequested += request =>
                _ = DetectPinnedPrivacyAsync(session, request, region,
                    AnnotationPreferencesProvider().AutoSelectPrivacyMosaic, session.PrivacyDetectionToken);
            session.RegisterFrame(region, EffectivePixels);
            session.NotifySelection(region);
            if (_marksHidden) session.ToggleAnnotations();
            var layer = _sceneLayer;
            _hdrPreview?.SetFrame(null);
            _annotationSurface = new(session, region, RenderScaling / CurrentPinZoom, args =>
            {
                var point = args.GetPosition(layer);
                var display = DisplayRegion;
                return new(display.X + (int)Math.Floor(point.X * RenderScaling / CurrentPinZoom),
                    display.Y + (int)Math.Floor(point.Y * RenderScaling / CurrentPinZoom));
            });
            layer.Children.Add(_annotationSurface);

            session.CompleteRequested += action => _ = RunAsync(() => CompleteAnnotationAsync(action));
            session.RecognitionRequested += mode => _ = RunAsync(() => RecognizeAsync(mode));
            session.TranslationRequested += () => _ = RunAsync(TranslateAsync);
            InitializeAnnotationPreview(session);
            return session;
        }

        private async Task CompleteAnnotationAsync(ScreenshotOutputAction action)
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (action)
            {
                case ScreenshotOutputAction.Save: await SaveAsync(); break;
                case ScreenshotOutputAction.Copy:
                    await CopyAsync();
                    break;
                case ScreenshotOutputAction.Pin: await ApplyAnnotationAsync(); break;
                case ScreenshotOutputAction.QuickSave:
                    await QuickSaveAsync();
                    break;
                case ScreenshotOutputAction.ExportPdf:
                    await ExportDocumentAsync();
                    break;
                case ScreenshotOutputAction.Print:
                    await PrintDocumentAsync();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        private Task ApplyAnnotationAsync()
        {
            if (_annotation is { IsDrawing: false }) EndAnnotation();
            return Task.CompletedTask;
        }

        private void EndAnnotation()
        {
            var session = _annotation;
            if (session is not null)
            {
                session.CommitTextEdit();
                session.CancelGesture();
                session.ClearSelectedAnnotation();
                var preferences = session.CapturePinnedPreferences(AnnotationPreferencesProvider());
                _annotation = null;
                session.CanAnnotate = false;
                _annotationToolbar?.Dispose();
                _annotationToolbar = null;
                if (!_closed)
                {
                    UpdateSize();
                    RefreshHdrPreview();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }

                try
                {
                    AnnotationPreferencesChanged?.Invoke(preferences);
                }
                catch (Exception error)
                {
                    PinLogger.ZLogWarning(error, $"Pinned annotation preferences could not be saved.");
                    if (!_closed)
                    {
                        _status.Text = error.Message;
                        _status.IsVisible = true;
                    }
                }
            }

            if (!_closed) return;
            StopAnnotationPreview();
            _annotationCancellation?.Cancel();
            _annotationCancellation?.Dispose();
            _annotationCancellation = null;
            ClearAnnotationPrecisionSource();
            _annotationSurface?.Dispose();
            _annotationSurface = null;
            _annotationScene?.Dispose();
            _annotationScene = null;
        }

        private void AnnotationKeyDown(KeyEventArgs args)
        {
            if (_annotation is not { } session || args.Handled || BuiltinShortcutTextInput.OwnsKey(args)) return;
            if (session.HandleNumberCaptionKey(args.Key, args.KeyModifiers, true))
            {
                args.Handled = true;
                return;
            }

            var hasAnnotationBinding = SelectionCommands.TryResolvePinnedAnnotation(args.Key, args.KeyModifiers,
                out var definition, session);
            if (hasAnnotationBinding && definition!.Command == SelectionCommand.TogglePolylineArrows)
            {
                args.Handled = session.HandlePolylineArrowKey(args.Key);
                return;
            }

            var pinAction = BuiltinShortcuts.ResolvePinnedAnnotation(BuiltinShortcuts.Snapshot(), args);
            if (pinAction is not null && Presentation.Execute(pinAction))
            {
                args.Handled = true;
                return;
            }

            switch (pinAction)
            {
                case "wheel-up":
                    args.Handled = _simulatedWheel.Raise(1);
                    return;
                case "wheel-down":
                    args.Handled = _simulatedWheel.Raise(-1);
                    return;
                case "print" when ScreenshotPrintService.IsSupported:
                    args.Handled = true;
                    session.RequestComplete(ScreenshotOutputAction.Print);
                    return;
                case "close":
                    args.Handled = true;
                    RequestClose();
                    return;
                case "annotate-toolbar":
                    args.Handled = true;
                    _ = RunAsync(ToggleAnnotationToolbarAsync);
                    return;
                case "copy-all-text":
                    args.Handled = true;
                    _ = RunAsync(CopyRecognizedTextAsync);
                    return;
            }

            if (!hasAnnotationBinding) return;
            args.Handled = true;
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (definition!.Command)
            {
                case SelectionCommand.Cancel:
                    if (session.IsDrawing) session.CancelGesture();
                    else EndAnnotation();
                    break;
                case SelectionCommand.Confirm: _ = RunAsync(ApplyAnnotationAsync); break;
                case SelectionCommand.Undo: session.Undo(); break;
                case SelectionCommand.Redo: session.Redo(); break;
                case SelectionCommand.Clear: session.DeleteSelectedAnnotation(); break;
                case SelectionCommand.ClearAnnotations: session.ClearAnnotations(); break;
                case SelectionCommand.ToggleAnnotations: session.ToggleAnnotations(); break;
                case SelectionCommand.RenumberAnnotations: session.RenumberAnnotations(); break;
                case SelectionCommand.FinishPolyline: session.FinishPolyline(); break;
                case SelectionCommand.RecognizeText: session.RequestRecognition(RecognitionMode.Text); break;
                case SelectionCommand.Translate: session.RequestTranslation(); break;
                case SelectionCommand.RecognizeQr: session.RequestRecognition(RecognitionMode.Barcode); break;
                case SelectionCommand.RecognizeTextAndCopy: _ = RunAsync(CopyRecognizedTextAsync); break;
                case SelectionCommand.SelectTool:
                    session.SelectDrawingTool(definition.Tool ?? ScreenshotTool.Select); break;
                case SelectionCommand.Copy: session.RequestComplete(ScreenshotOutputAction.Copy); break;
                case SelectionCommand.Save: session.RequestComplete(ScreenshotOutputAction.Save); break;
                case SelectionCommand.QuickSave: session.RequestComplete(ScreenshotOutputAction.QuickSave); break;
                default: args.Handled = false; break;
            }
        }
    }
}
