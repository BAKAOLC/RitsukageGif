using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private readonly HashSet<Key> _annotationTextInputKeys = [];
        private ScreenshotAnnotationSession? _annotation;
        private CancellationTokenSource? _annotationCancellation;
        private Grid? _annotationFooter;
        private Avalonia.PixelPoint _annotationPosition;
        private ScreenshotAnnotationSurface? _annotationSurface;
        private ScreenshotToolbar? _annotationToolbar;
        public Func<AnnotationPreferences> AnnotationPreferencesProvider { get; set; } = () => new();
        public Action<AnnotationPreferences>? AnnotationPreferencesChanged { get; set; }

        private void InitializeAnnotationKeyboard()
        {
            AddHandler(KeyDownEvent, (_, args) =>
            {
                if (IsAnnotationTextInputKey(args.Key)) _annotationTextInputKeys.Add(args.Key);
            }, RoutingStrategies.Tunnel, true);
            AddHandler(KeyUpEvent, (_, args) => _annotationTextInputKeys.Remove(args.Key),
                RoutingStrategies.Tunnel, true);
        }

        private bool IsAnnotationTextInputKey(Key key)
        {
            return _annotation is { IsEditingText: true } || _annotationTextInputKeys.Contains(key) ||
                   FocusManager?.GetFocusedElement() is TextBox;
        }

        private Task BeginAnnotationAsync()
        {
            try
            {
                return BeginAnnotationCore();
            }
            catch
            {
                EndAnnotation();
                throw;
            }
        }

        private Task BeginAnnotationCore()
        {
            if (_annotation is not null || IsOperationLocked(PinnedLockedOperations.Annotation) || IsMouseThrough)
                return Task.CompletedTask;
            ClearTextSelection();
            _thumbnailBeforeAnnotation = _isThumbnail;
            _isThumbnail = false;
            UpdateSize();
            _annotationPosition = Position;
            _annotationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var region = new PixelRect(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height);
            var session = _annotation = new() { CanScroll = false, Tool = ScreenshotTool.Brush };
            var annotationPreferences = AnnotationPreferencesProvider();
            session.ApplyPinnedPreferences(annotationPreferences);
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
                    annotationPreferences.AutoSelectPrivacyMosaic, session.PrivacyDetectionToken);
            session.RegisterFrame(region, _pixels);
            session.NotifySelection(region);
            var layer = new Grid { Width = _image.Width, Height = _image.Height };
            _viewport.Content = null;
            _hdrLayer?.Children.Remove(_image);
            _hdrPreview?.SetFrame(null);
            if (_hdrSource is not null)
                _hdrIndicator.Bind(TextBlock.TextProperty, LiveText.Resource("PinnedImageHdrEditing"));
            layer.Children.Add(_image);
            var surface = _annotationSurface = new(session, region, RenderScaling / _zoom, args =>
            {
                var point = args.GetPosition(layer);
                return new((int)Math.Floor(point.X * RenderScaling / _zoom),
                    (int)Math.Floor(point.Y * RenderScaling / _zoom));
            });
            layer.Children.Add(surface);
            _viewport.Content = layer;
            var toolbar = _annotationToolbar = new(session, region, RenderScaling / _zoom,
                SelectionCommands.SnapshotBindings(), imageEditing: true) { IsDocked = true };
            toolbar.Refresh(region, default, true);
            var apply = new Button
                { Name = "PinnedImageApply", [!ContentProperty] = LiveText.Resource("AnimationApplyDraft") };
            var cancel = new Button
                { Name = "PinnedImageDiscard", [!ContentProperty] = LiveText.Resource("AnimationDiscardDraft") };
            apply.Click += async (_, _) => await RunAsync(ApplyAnnotationAsync);
            cancel.Click += (_, _) => EndAnnotation();
            session.CompleteRequested += action => _ = RunAsync(() => CompleteAnnotationAsync(action));
            session.RecognitionRequested += mode => _ = RunAsync(() => RecognizeAsync(mode));
            var footer = _annotationFooter = new() { RowDefinitions = new("Auto,Auto") };
            footer.Children.Add(toolbar);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8, Margin = new(8), Children = { apply, cancel },
            };
            Grid.SetRow(buttons, 1);
            footer.Children.Add(buttons);
            _content.RowDefinitions = new(_hdrSource is null ? "*,Auto" : "*,Auto,Auto");
            Grid.SetRow(footer, _hdrSource is null ? 1 : 2);
            _content.Children.Add(footer);
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            Width = Math.Max(Width, Math.Min(720, (screen?.WorkingArea.Width ?? 1280) / RenderScaling * 0.9));
            Height = Math.Max(Height, Math.Min(480, (screen?.WorkingArea.Height ?? 720) / RenderScaling * 0.9));
            RestoreToScreen();
            InitializeAnnotationPreview(session);
            return Task.CompletedTask;
        }

        private async Task<byte[]> ComposeAnnotationAsync(bool keepBusy = false, bool preserveHdr = true)
        {
            var session = _annotation ?? throw new InvalidOperationException();
            session.SetExportState(true);
            var token = _annotationCancellation!.Token;
            try
            {
                if (preserveHdr && _hdrSource is { } hdr)
                {
                    if (!session.HasAnnotations) return _png;
                    var composed = await session.ComposeHdrAsync(session.Selection, hdr, token: token);
                    return await HdrImageService.EncodePngAsync(composed, token);
                }

                var sourcePng = _hdrSource is null ? _png : _sdrPng ?? _png;
                return await session.ComposeAsync(session.Selection, sourcePng, token);
            }
            finally
            {
                if (!keepBusy && ReferenceEquals(_annotation, session)) session.SetExportState(false);
            }
        }

        private async Task CompleteAnnotationAsync(ScreenshotOutputAction action)
        {
            switch (action)
            {
                case ScreenshotOutputAction.Save: await SaveAsync(); break;
                case ScreenshotOutputAction.Copy:
                    await CopyAsync();
                    break;
                case ScreenshotOutputAction.Pin: await ApplyAnnotationAsync(); break;
                case ScreenshotOutputAction.QuickSave:
                    throw new NotSupportedException("Quick save is not an annotation completion action.");
                default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        private async Task ApplyAnnotationAsync()
        {
            if (_annotation is null || _annotation.IsDrawing) return;
            var session = _annotation;
            var token = _annotationCancellation!.Token;
            try
            {
                var png = await ComposeAnnotationAsync(true);
                var decoded = await Task.Run(() => PinnedImageContent.Decode(png, token), token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(session, _annotation)) return;
                var bitmap =
                    new ScreenshotTiledImage(decoded.Width, decoded.Height, decoded.Pixels, AlphaFormat.Unpremul);
                Bitmap thumbnail;
                try
                {
                    using var stream = new MemoryStream(decoded.ThumbnailPng, false);
                    thumbnail = new(stream);
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }

                EndAnnotation();
                var previousBitmap = _bitmap;
                var previousThumbnail = _thumbnail;
                _bitmap = bitmap;
                _croppedImage = null;
                _thumbnail = thumbnail;
                _png = decoded.Png;
                _pixels = decoded.Pixels;
                ResetTextRecognition();
                _sdrPng = decoded.SdrPng;
                ContentHash = decoded.ContentHash;
                ConfigureHdr(decoded.Hdr);
                Id = Guid.NewGuid().ToString("N");
                UpdateSize();
                previousBitmap.Dispose();
                previousThumbnail.Dispose();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                if (ReferenceEquals(_annotation, session)) session.SetExportState(false);
            }
        }

        private void EndAnnotation()
        {
            if (_annotation is null) return;
            var session = _annotation;
            session.CancelTextEdit();
            var preferences = session.CapturePinnedPreferences(AnnotationPreferencesProvider());
            StopAnnotationPreview();
            _annotation = null;
            _isThumbnail = _thumbnailBeforeAnnotation;
            _annotationCancellation?.Cancel();
            _annotationCancellation?.Dispose();
            _annotationCancellation = null;
            ClearAnnotationPrecisionSource();
            _annotationSurface?.Dispose();
            _annotationSurface = null;
            _annotationToolbar?.Dispose();
            _annotationToolbar = null;
            if (_viewport.Content is Grid layer) layer.Children.Clear();
            if (_hdrLayer is not null)
            {
                _hdrLayer.Children.Insert(0, _image);
                _viewport.Content = _hdrLayer;
                RefreshHdrPreview();
            }
            else
            {
                _viewport.Content = _image;
            }

            _content.RowDefinitions = new(_hdrSource is null ? "*" : "*,Auto");
            if (_annotationFooter is not null) _content.Children.Remove(_annotationFooter);
            _annotationFooter = null;
            session.Dispose();
            if (!_closed)
            {
                Position = _annotationPosition;
                UpdateSize();
                RestoreToScreen();
            }

            try
            {
                AnnotationPreferencesChanged?.Invoke(preferences);
            }
            catch (Exception error)
            {
                if (!_closed)
                {
                    _status.Text = error.Message;
                    _status.IsVisible = true;
                }
            }
        }

        private void AnnotationKeyDown(KeyEventArgs args)
        {
            if (_annotation is not { } session || args.Source is TextBox) return;
            if (args is { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
            {
                args.Handled = true;
                if (session.IsDrawing) session.CancelGesture();
                else EndAnnotation();
                return;
            }

            switch (BuiltinShortcuts.Resolve(BuiltinShortcuts.Snapshot(), "BuiltinShortcutPinnedContext", args))
            {
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

            if (!SelectionCommands.TryResolve(args.Key, args.KeyModifiers, out var definition)) return;
            args.Handled = true;
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (definition!.Command)
            {
                case SelectionCommand.Cancel:
                    if (session.IsDrawing) session.CancelGesture();
                    else EndAnnotation();
                    break;
                case SelectionCommand.Confirm when session.IsPrivacyDetectionActive:
                    _ = session.ApplyPrivacyMosaicAsync();
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
                case SelectionCommand.RecognizeQr: session.RequestRecognition(RecognitionMode.Barcode); break;
                case SelectionCommand.RecognizeTextAndCopy: _ = RunAsync(CopyRecognizedTextAsync); break;
                case SelectionCommand.SelectTool: session.Tool = definition.Tool ?? ScreenshotTool.Select; break;
                case SelectionCommand.Copy: session.RequestComplete(ScreenshotOutputAction.Copy); break;
                case SelectionCommand.Save: session.RequestComplete(ScreenshotOutputAction.Save); break;
                default: args.Handled = false; break;
            }
        }
    }
}
