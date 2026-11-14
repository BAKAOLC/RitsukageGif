using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private bool _replacingOverlays;
        private bool _scrolling;
        private Task? _scrollingTask;
        internal ScrollCapturePreferences ScrollCapturePreferences { get; set; } = new();
        internal Action<bool>? ScrollAutoCropChanged { get; set; }
        internal Action<bool>? ScrollClearConfirmationChanged { get; set; }
        internal Action<ScrollCaptureWarningKind>? ScrollWarningSuppressed { get; set; }
        internal Func<ScreenshotResult, CancellationToken, Task>? ScrollingCloseOutputHandler { get; set; }
        internal bool ScreenshotPreview { get; private set; }
        internal bool WasScrollingScreenshot { get; private set; }
        private bool UsesLocalSource => ScreenshotPreview || DesktopCapture.UsesSourceSelection;

        private void SetScrollAutoCrop(bool enabled)
        {
            ScrollCapturePreferences = ScrollCapturePreferences with { AutoCropOnReverse = enabled };
            ScrollAutoCropChanged?.Invoke(enabled);
        }

        private void SetScrollClearConfirmation(bool skip)
        {
            ScrollCapturePreferences = ScrollCapturePreferences with { SkipClearConfirmation = skip };
            ScrollClearConfirmationChanged?.Invoke(skip);
        }

        private void SuppressScrollWarning(ScrollCaptureWarningKind kind)
        {
            ScrollCapturePreferences = ScrollCapturePreferences.SuppressWarning(kind);
            ScrollWarningSuppressed?.Invoke(kind);
        }

        internal void OverlayClosed()
        {
            if (!_replacingOverlays) Cancel();
        }

        private async Task StartScrollingAsync(bool? requestedVertical)
        {
            SelectionLogger.ZLogInformation(
                $"Scrolling capture requested. SelectionId={_selectionId} Region={Snapshot.Region} Finished={_finished} Scrolling={_scrolling} Busy={Annotations?.IsBusy} ColorMode={Annotations?.OutputColorMode.ToString()}");
            var initialAction = ScrollCapturePreferences.InitialAction;
            var vertical = requestedVertical ?? initialAction != ScrollCaptureInitialAction.Horizontal;
            if (_finished || _scrolling || _screenshotCompleting || ScreenshotPreview ||
                Annotations is not { CanStartScrolling: true } annotations) return;

            var colorMode = annotations.OutputColorMode;
            var selected = Snapshot.Region;
            if (vertical ? selected.Height < 16 || selected.Width < 8 : selected.Width < 16 || selected.Height < 8)
            {
                annotations.SetExportState(false, UiText.Get("ScrollCaptureAreaTooSmall"));
                return;
            }

            if ((long)selected.Width * selected.Height * (colorMode == OutputColorMode.Sdr ? 12 : 36) >
                256L * 1024 * 1024 ||
                (vertical ? selected.Height : selected.Width) > 32768)
            {
                annotations.SetExportState(false, UiText.Get("SelectionTooLarge"));
                return;
            }

            _scrolling = true;
            StopWindowsKeyGuard();
            _showing = true;
            annotations.SetExportState(true);
            ReleaseCaptures();
            _left = _right = _enter = _escape = false;
            _pendingFinish = false;
            _pendingResult = null;
            try
            {
                var region = Snapshot.Region;
                var captureScaling = ScreenshotCaptureScaling;
                var outputOrigin = ScreenshotOutputOrigin;
                var activeOverlay = _windows.FirstOrDefault(window => window.IsActive);
                var placement = activeOverlay is null ? null : _screens.ScreenFromWindow(activeOverlay);
                var preferredPlacementScreen = placement is null
                    ? activeOverlay?.Region
                    : new(placement.Bounds.X, placement.Bounds.Y, placement.Bounds.Width, placement.Bounds.Height);
                var captured = await ScreenshotScrollingWindow.RunAsync(_screens, region,
                    vertical ? ScrollCaptureAxis.Vertical : ScrollCaptureAxis.Horizontal,
                    WindowSource && !DesktopCapture.UsesSourceSelection
                        ? Snapshot.Target ?? throw new CaptureSourceLostException()
                        : null, _cancel.Token, _screenshotToneMap, _cursor ? ScreenshotCursor : null,
                    ScrollCapturePreferences.RespondShortcuts, preferredPlacementScreen,
                    AppearancePreferences,
                    HideScreenshotOverlaysForScrollingAsync,
                    (owner, image, action, token) => OutputScrollingCaptureAsync(owner, image, action,
                        captureScaling, colorMode,
                        DesktopCapture.UsesSourceSelection
                            ? null
                            : (owner as ScreenshotScrollingWindow)?.CaptureOutputOrigin ?? outputOrigin, token),
                    ScrollCapturePreferences.AutoCropOnReverse, SetScrollAutoCrop, colorMode, initialAction,
                    ScrollCapturePreferences.SkipClearConfirmation, SetScrollClearConfirmation,
                    ScrollingCloseOutputHandler is not null, ScrollCapturePreferences, SuppressScrollWarning);
                SelectionLogger.ZLogInformation(
                    $"Scrolling capture returned. SelectionId={_selectionId} Finished={_finished} HasFrame={captured is not null} Action={captured?.Action.ToString()}");
                if (_finished) return;
                if (captured is null)
                {
                    Finish(null);
                    return;
                }

                Finish(SelectionResult.Create(region, WindowSource, Snapshot.Target, UsesLocalSource, _cursor));
            }
            catch (OperationCanceledException) when (_finished || _cancel.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                SelectionLogger.ZLogError(error, $"Scrolling capture transition failed. SelectionId={_selectionId}");
                if (!_finished) Finish(null, error);
            }
            finally
            {
                _scrolling = false;
                if (!_finished)
                {
                    if (Annotations is { IsBusy: true }) Annotations.SetExportState(false);
                    foreach (var window in _windows) window.Show();
                    await StartWindowsKeyGuardAsync();
                    _windows.FirstOrDefault()?.Activate();
                    Refresh();
                }

                _showing = false;
            }
        }

        private async Task<bool> OutputScrollingCaptureAsync(Window owner,
            ScrollCaptureImage image, ScrollingCaptureAction action, double captureScaling, OutputColorMode colorMode,
            PixelPoint? outputOrigin, CancellationToken token)
        {
            if (_finished) return false;
            var bounds = new PixelRect(0, 0, image.Width, image.Height);
            var png = await Annotations!.ComposeAsync(bounds, image, token);
            var hdr = colorMode == OutputColorMode.Sdr
                ? null
                : image.Hdr is { } source
                    ? await Annotations.ComposeHdrAsync(bounds, source, token: token)
                    : throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
            token.ThrowIfCancellationRequested();
            WasScrollingScreenshot = true;
            ScreenshotOutputAction outputAction;
            switch (action)
            {
                case ScrollingCaptureAction.Close:
                    if (ScrollingCloseOutputHandler is { } closeOutput)
                        await closeOutput(new(png, ScreenshotOutputAction.Edit, hdr)
                        {
                            ColorMode = colorMode, CaptureScaling = captureScaling,
                            OutputOrigin = outputOrigin, Decoration = Decoration, OutputRegion = bounds,
                        }, token);
                    return true;
                case ScrollingCaptureAction.OcrCopy:
                    var recognition = await RecognitionOutput.RecognizeAsync(png, RecognitionPreferences,
                        new OcrRecognitionService(), token);
                    var text = RecognitionTextFormatter.Format(recognition, RecognitionPreferences);
                    token.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(text))
                        throw new InvalidOperationException(UiText.Get("RecognitionEmpty"));
                    if (RecognitionPreferences.ShowDialogOnRecognition)
                    {
                        await RecognitionWindow.ShowAsync(owner, png, RecognitionMode.Text,
                            preferences: RecognitionPreferences, initialResult: recognition, token: token,
                            translationPreferences: TranslationPreferences,
                            originalImageVisibilityChanged: RecognitionOriginalImageVisibilityChanged,
                            copyDirectlyNextTime: () =>
                                DirectRecognitionCopyRequested?.Invoke(RecognitionConfirmationKind.Screenshot),
                            dialogPreferencesChanged: RecognitionDialogPreferencesChanged);
                    }
                    else
                    {
                        var clipboard = owner.Clipboard ??
                                        throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                        await clipboard.SetTextAsync(text);
                        await clipboard.FlushAsync();
                    }

                    if (RecognitionOutputHandler is { } recognized)
                        await recognized(new(png, ScreenshotOutputAction.Copy)
                        {
                            ColorMode = OutputColorMode.Sdr, CaptureScaling = captureScaling,
                            OutputOrigin = outputOrigin, Decoration = Decoration,
                            OutputRegion = bounds,
                        }, ScreenshotAutoSaveTriggers.Scrolling, token);
                    return true;
                case ScrollingCaptureAction.Copy:
                    outputAction = ScreenshotOutputAction.Copy;
                    break;
                case ScrollingCaptureAction.Save:
                    outputAction = ScreenshotOutputAction.Save;
                    break;
                case ScrollingCaptureAction.QuickSave:
                    outputAction = ScreenshotOutputAction.QuickSave;
                    break;
                case ScrollingCaptureAction.Pin:
                    outputAction = ScreenshotOutputAction.Pin;
                    break;
                case ScrollingCaptureAction.Edit:
                    outputAction = ScreenshotOutputAction.Edit;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }

            var result = new ScreenshotResult(png, outputAction, hdr)
            {
                ColorMode = colorMode, CaptureScaling = captureScaling,
                OutputOrigin = outputOrigin, Decoration = Decoration,
                OutputRegion = bounds,
            };
            return ScreenshotOutputHandler is not { } output || await output(owner, result, token);
        }

        private async Task HideScreenshotOverlaysForScrollingAsync(CancellationToken token)
        {
            foreach (var window in _windows.ToArray())
                await CaptureWindowVisibility.HideAsync(window, token);
            SelectionLogger.ZLogDebug(
                $"Scrolling capture overlay handoff completed. SelectionId={_selectionId} OverlayCount={_windows.Count}");
        }

        private void ReplaceScreenshotPreview(ScrollCaptureImage frame, double captureScaling = 1,
            PixelPoint? outputOrigin = null, OutputColorMode colorMode = OutputColorMode.Sdr)
        {
            _selectionShape = null;
            _shapePoints.Clear();
            _regionEditBase = null;
            _selectionWindows.Clear();
            ShapeMode = SelectionShapeMode.Rectangle;
            Dispatcher.UIThread.VerifyAccess();
            var oldAnnotations = Annotations;
            var region = new PixelRect(0, 0, frame.Width, frame.Height);
            var working = _screens.Primary?.WorkingArea;
            var dpi = _screens.Primary?.Scaling ?? 1;
            var scale = Math.Max(1, Math.Min(frame.Width / ((working?.Width ?? 1280) / dpi * .75),
                frame.Height / ((working?.Height ?? 800) / dpi * .65)));
            OverlayWindow replacement;
            ScreenshotPreview = true;
            try
            {
                InitializeAnnotations();
                Annotations!.CanScroll = false;
                Annotations.CanOutputHdr = frame.Hdr is not null;
                Annotations.OutputColorMode = colorMode;
                replacement = new(this, region, scale, frame.Pixels, _radius, _threshold, true);
            }
            catch
            {
                Annotations?.Dispose();
                Annotations = oldAnnotations;
                ScreenshotPreview = false;
                throw;
            }

            _replacingOverlays = true;
            try
            {
                foreach (var window in _windows)
                {
                    window.Close();
                    window.DisposeImage();
                }

                _windows.Clear();
            }
            finally
            {
                _replacingOverlays = false;
            }

            oldAnnotations?.Dispose();
            _screenshots.Clear();
            _nativeScreenshots.Clear();
            _previewOriginalPng = null;
            _previewOutputOrigin = outputOrigin;
            _previewCaptureScaling =
                double.IsFinite(captureScaling) && captureScaling is >= 1 and <= 16 ? captureScaling : 1;
            _screenshots.Add((region, frame.Pixels));
            _hdrScreenshots.Clear();
            _historyPreviewHdr = frame.Hdr is not null;
            if (frame.Hdr is { } hdr) _hdrScreenshots.Add((region, hdr));
            WasScrollingScreenshot = false;
            ScreenshotPreview = true;
            WindowSource = false;
            Controls = false;
            _targets = [];
            _hoveredTarget = _selectedTarget = null;
            _selectionIsWholeTarget = false;
            _hovered = region;
            _state.Clear();
            _state.Begin(default, default, 0);
            _state.End(default, default, region);
            _lastSnapshot = null;
            _windows.Add(replacement);
            Annotations.NotifySelection(region);
        }
    }
}
