using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private static readonly IBrush TextSelectionBrush = AppTheme.Accent;
        private static readonly ITextRecognitionService DefaultTextRecognition = new OcrRecognitionService();
        private OcrRecognitionResult? _recognizedText;
        private ImageTextSelection _textSelection = new(ImageTextSelectionDocument.Empty);
        private bool _pendingTextDrag;
        private PointerPressedEventArgs? _textSelectionDragTrigger;
        private bool _selectingText;
        private bool _textRecognitionAttempted;
        private bool _textRecognitionInitialized;
        private CancellationTokenSource? _textRecognitionCancellation;
        private PinnedTextSelectionSurface? _textSelectionSurface;
        public Func<ITextRecognitionService>? TextRecognitionServiceProvider { get; set; }
        public Func<RecognitionPreferences> RecognitionPreferencesProvider { get; set; } = () => new();

        private bool HasTextSelection => ActiveTextSelection.HasSelection;

        private bool CanSelectImageText => !LiveCaptureActive && !IsMarkEditing && OcrTextSelectable &&
                                           (!_isThumbnail || CurrentPreferences.ThumbnailSelectableText);

        private void EnsureTextRecognition(bool force = false, string? language = null)
        {
            if (_closed || _annotation is not null ||
                (!force && (_textRecognitionAttempted || _activeTranslatedRaster is not null ||
                            !CurrentPreferences.SelectableText || !CanSelectImageText))) return;
            var preferences = RecognitionPreferencesProvider();
            if (language is not null) preferences = preferences with { LanguageTag = language };
            _textRecognitionInitialized = true;
            ResetTextRecognition();
            preferences = preferences.Normalize();
            _textRecognitionAttempted = true;
            var cancellation = _textRecognitionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var pixels = _pixels;
            var width = (int)_bitmap.Size.Width;
            var height = (int)_bitmap.Size.Height;
            var service = TextRecognitionServiceProvider?.Invoke() ?? DefaultTextRecognition;
            PinLogger.ZLogDebug($"Pinned image text recognition started. Width={width} Height={height}");
            _ = RecognizeSelectableTextAsync(service, pixels, width, height, preferences, cancellation);
        }

        private async Task RecognizeSelectableTextAsync(ITextRecognitionService service, byte[] pixels,
            int width, int height, RecognitionPreferences preferences, CancellationTokenSource cancellation)
        {
            try
            {
                var capabilities = await service.GetCapabilitiesAsync(cancellation.Token).ConfigureAwait(false);
                var result = await service.RecognizeBgraAsync(pixels, width, height, checked(width * 4),
                        RecognitionOutput.Language(preferences, capabilities.Languages), cancellation.Token)
                    .ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_closed || !ReferenceEquals(_textRecognitionCancellation, cancellation)) return;
                    _recognizedText = result;
                    _textSelection = new(ImageTextSelectionBuilder.FromOcr(result));
                    if (result.Lines.Count > 0 || result.LanguageTag.Length > 0)
                    {
                        _ocrTextSelectable ??= CurrentPreferences.SelectableText;
                        RefreshOcrLayerVisibility();
                        RefreshHdrPreview();
                        StateChanged?.Invoke(this, EventArgs.Empty);
                    }

                    PinLogger.ZLogDebug($"Pinned image text recognition completed. LineCount={result.Lines.Count}");
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                PinLogger.ZLogWarning(error, $"Pinned image text recognition failed.");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_closed && ReferenceEquals(_textRecognitionCancellation, cancellation))
                        ToolTip.SetTip(_image, error.Message);
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ReferenceEquals(_textRecognitionCancellation, cancellation))
                        _textRecognitionCancellation = null;
                });
                cancellation.Dispose();
            }
        }

        private void ResetTextRecognition()
        {
            _textRecognitionCancellation?.Cancel();
            _textRecognitionCancellation = null;
            _textRecognitionAttempted = false;
            _recognizedText = null;
            ClearTextSelection();
            _textSelection = new(ImageTextSelectionDocument.Empty);
            if (ReferenceEquals(_image.Cursor, TextSelectionCursor)) _image.Cursor = null;
        }

        private Point TextImagePoint(Point point)
        {
            return new(
                DisplayRegion.X + point.X * RenderScaling / CurrentPinZoom,
                DisplayRegion.Y + point.Y * RenderScaling / CurrentPinZoom);
        }

        private int HitRecognizedText(Point point)
        {
            return ActiveTextSelection.Hit(TextImagePoint(point));
        }

        private static bool IgnoreRecognizedText(PointerPressedEventArgs args)
        {
            var kind = args.ClickCount >= 2 ? MouseGestureKind.LeftDoubleClick : MouseGestureKind.LeftClick;
            foreach (var binding in MouseBindings)
            {
                if (binding.Gesture.Modifiers != args.KeyModifiers) continue;
                switch (binding)
                {
                    case { Action: MouseAction.MoveWindowIgnoringText, Gesture.Kind: MouseGestureKind.LeftDrag }:
                    case { Action: MouseAction.ToggleThumbnail, Gesture.Kind: var configured } when configured == kind:
                        return true;
                }
            }

            return false;
        }

        private bool TryBeginTextSelection(PointerPressedEventArgs args)
        {
            if (_regionSelectionMode || !CanSelectImageText ||
                (_activeTranslatedRaster is null && _recognizedText is null) ||
                args.GetCurrentPoint(_image).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
                IgnoreRecognizedText(args)) return false;
            var edges = GetResizeEdges(args.GetPosition(this));
            if (edges.Horizontal != 0 || edges.Vertical != 0) return false;
            _textSelectionMenu?.IsOpen = false;
            var point = TextImagePoint(args.GetPosition(_image));
            if (args.ClickCount == 2 && ActiveTextSelection.SelectLine(point))
            {
                _textSelectionMarkPreview = false;
                ShowTextSelection();
                ShowTextSelectionMenu();
                args.Handled = true;
                return true;
            }

            _pendingTextDrag = ActiveTextSelection.ContainsSelected(point);
            if (!ActiveTextSelection.Begin(point)) return false;
            _selectingText = true;
            _textSelectionDragTrigger = args;
            args.Pointer.Capture(_image);
            args.Handled = true;
            return true;
        }

        private void ShowTextSelection()
        {
            if (_textSelectionSurface is null)
            {
                _textSelectionSurface = new(DrawTextSelection) { IsHitTestVisible = false, ClipToBounds = true };
                _content.Children.Add(_textSelectionSurface);
            }

            _textSelectionSurface.IsVisible = HasTextSelection || HasTextMarkup;
            if (HasTextSelection)
            {
                _hdrPreviewWork?.Cancel();
                _hdrPreview?.SetFrame(null);
                if (ProcessingSourceHdr is not null)
                    _hdrIndicator.Bind(TextBlock.TextProperty, LiveText.Resource("PinnedImageSdrPreview"));
            }
            else
            {
                RefreshHdrPreview();
            }

            _textSelectionSurface.InvalidateVisual();

            InvalidateInputUi();
        }

        private bool ContinueTextSelection(PointerEventArgs args)
        {
            if (!_selectingText) return false;
            if (_pendingTextDrag && _textSelectionDragTrigger is { } trigger)
            {
                var text = ActiveTextSelection.SelectedText;
                _pendingTextDrag = false;
                _selectingText = false;
                _textSelectionDragTrigger = null;
                _ = RunAsync(() => DragSelectedTextAsync(trigger, text));
            }
            else if (ActiveTextSelection.Extend(TextImagePoint(args.GetPosition(_image))))
            {
                _textSelectionMarkPreview = false;
                ShowTextSelection();
            }

            args.Handled = true;
            return true;
        }

        private bool FinishTextSelection(PointerReleasedEventArgs args)
        {
            if (!_selectingText) return false;
            FinishTextSelectionGesture();
            args.Pointer.Capture(null);
            ShowTextSelectionMenu();
            args.Handled = true;
            return true;
        }

        private void FinishTextSelectionGesture()
        {
            if (!_selectingText) return;
            _selectingText = false;
            _pendingTextDrag = false;
            _textSelectionDragTrigger = null;
            ActiveTextSelection.Finish();
            if (_textSelectionSurface is { } surface)
            {
                surface.IsVisible = HasTextSelection || HasTextMarkup;
                surface.InvalidateVisual();
                InvalidateInputUi();
            }

            if (!HasTextSelection) RefreshHdrPreview();
        }

        private void ClearTextSelection()
        {
            _textSelectionMenu?.IsOpen = false;
            var hadSelection = HasTextSelection;
            _selectingText = false;
            _pendingTextDrag = false;
            _textSelectionDragTrigger = null;
            ActiveTextSelection.Clear();
            _textSelectionMarkPreview = false;
            if (_textSelectionSurface is { } surface)
            {
                surface.IsVisible = HasTextMarkup;
                surface.InvalidateVisual();
                InvalidateInputUi();
            }

            if (hadSelection && !_closed && _annotation is null) RefreshHdrPreview();
        }

        private void DrawTextSelection(DrawingContext context)
        {
            var scale = CurrentPinZoom / RenderScaling;
            var region = DisplayRegion;
            using var clip = context.PushClip(_viewport.Bounds);
            if (_image.TransformToVisual(_textSelectionSurface!) is not { } viewMatrix) return;
            using var view = context.PushTransform(viewMatrix);
            RenderTextMarkup(context, scale, region);
            if (_textSelectionMarkPreview) return;
            using var opacity = context.PushOpacity(0.4);
            foreach (var span in ActiveTextSelection.SelectedSpans)
            {
                var bounds = span.Bounds;
                var rectangle = new Rect((bounds.X - region.X) * scale,
                    (bounds.Y - region.Y) * scale, bounds.Width * scale, bounds.Height * scale);
                context.DrawRectangle(TextSelectionBrush, null, rectangle);
            }
        }

        private string SelectedRecognizedText()
        {
            return ActiveTextSelection.SelectedText;
        }

        private async Task DragSelectedTextAsync(PointerPressedEventArgs args, string text)
        {
            var transfer = new DataTransfer();
            var item = new DataTransferItem();
            item.Set(DataFormat.Text, text);
            transfer.Add(item);
            _mouseGesture.Reset();
            _pressedButtons.Clear();
            args.Pointer.Capture(null);
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Copy | DragDropEffects.Move);
        }

        private async Task CopySelectedTextAsync()
        {
            if (!HasTextSelection) return;
            var preferences = RecognitionPreferencesProvider().Normalize();
            var text = RecognitionTextFormatter.Format(SelectedRecognizedText(), preferences);
            var output = AutomaticSave is not null &&
                         (SavePreferencesProvider().Normalize().AutoSaveTriggers &
                          ScreenshotAutoSaveTriggers.Recognize) != 0
                ? await CaptureOutputAsync(OutputColorMode.Sdr, _lifetime.Token)
                : null;
            if (_activeTranslatedRaster is null && preferences.ShowDialogOnCopySelected)
            {
                await RecognitionWindow.ShowAsync(this, ReadOnlyMemory<byte>.Empty, RecognitionMode.Text,
                    preferences: preferences, initialText: text, token: _lifetime.Token,
                    copyDirectlyNextTime: () =>
                        DirectRecognitionCopyRequested?.Invoke(RecognitionConfirmationKind.PinSelectedText),
                    dialogPreferencesChanged: RecognitionDialogPreferencesChanged);
            }
            else
            {
                var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                await clipboard.SetTextAsync(text);
                await clipboard.FlushAsync();
            }

            if (output is not null) await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Recognize, output);
        }
    }
}
