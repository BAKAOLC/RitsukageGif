using Avalonia.Controls;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private bool _annotationSelectionSubscribed;
        private ScreenshotOutputAction _screenshotAction = ScreenshotOutputAction.Copy;
        private bool _screenshotCompleting;
        private Task? _screenshotTask;
        private CancellationTokenSource? _screenshotWork;

        internal ScreenshotOutputAction ConfirmAction
        {
            get => _screenshotAction;
            init => _screenshotAction = value;
        }

        internal ScreenshotAnnotationSession? Annotations { get; private set; }
        internal Func<Window?, ScreenshotResult, CancellationToken, Task<bool>>? ScreenshotOutputHandler { get; set; }

        private Window? ScreenshotOwner =>
            _windows.FirstOrDefault(window => window.IsActive) ?? _windows.FirstOrDefault();

        internal bool ScreenshotDrawing => Annotations is { Tool: not ScreenshotTool.Select } or { IsDrawing: true } or
            { HasSelectedAnnotation: true };

        internal bool OwnsScreenshotToolbar(OverlayWindow window)
        {
            var snapshot = Snapshot;
            if (snapshot.Phase is not (SelectionPhase.Selected or SelectionPhase.Confirming)) return false;
            var corner = new PixelPoint(snapshot.Region.Right - 1, snapshot.Region.Bottom - 1);
            var owner = _windows.FirstOrDefault(item => item.Region.Contains(corner)) ??
                        _windows.FirstOrDefault(item => !item.Region.Intersect(snapshot.Region).IsEmpty);
            return ReferenceEquals(owner, window);
        }

        private void InitializeAnnotations()
        {
            if (!ScreenshotMode) return;
            Annotations = new();
            Annotations.ToolbarPreferencesChanged +=
                (preferences, tools) => AnnotationToolbarChanged?.Invoke(preferences, tools);
            Annotations.QuickMarkBoundsProvider = FindQuickMarkBounds;
            Annotations.PrivacyDetectionRequested += request =>
                _screenshotTask = DetectPrivacyAsync(request, Annotations, Annotations.PrivacyDetectionToken);
            Annotations.RecognitionRequested += mode => RecognizeSelection(mode);
            Annotations.StartScrollingRequested += vertical => _scrollingTask = StartScrollingAsync(vertical);
            Annotations.CompleteRequested += action =>
            {
                if (_finished || _screenshotCompleting) return;
                _screenshotAction = action;
                Confirm();
                Refresh();
            };
            if (_annotationSelectionSubscribed) return;
            _annotationSelectionSubscribed = true;
            SelectionChanged += snapshot =>
            {
                if (Annotations.Selection != snapshot.Region) Annotations.NotifySelection(snapshot.Region);
            };
        }

        private PixelRect? FindQuickMarkBounds(PixelPoint point)
        {
            if (_finished || UsesLocalSource || DetectionMode == CaptureDetectionMode.None) return null;
            var target = WindowSource
                ? _selectedTarget
                : _targets.FirstOrDefault(candidate => candidate.Bounds.Contains(point));
            if (target is null || !target.Bounds.Contains(point)) return null;
            var bounds = WindowSource ? target.Client : target.Bounds;
            switch (Controls)
            {
                case true when _controlPoint == point && _controlWindow == target.Handle && !_controlBounds.IsEmpty:
                    return bounds.Intersect(_controlBounds);
                case true when WindowControlLocator.Find(target, point, _cancel.Token) is { } control:
                    bounds = bounds.Intersect(control.Bounds);
                    break;
            }

            return bounds.IsEmpty ? null : bounds;
        }

        private bool HandleAnnotationCommand(SelectionCommandDefinition definition)
        {
            if (Annotations is not { } annotations) return false;
            var command = definition.Command;
            if (annotations.IsPrivacyDetectionActive && command == SelectionCommand.Confirm)
            {
                _ = annotations.ApplyPrivacyMosaicAsync();
                return true;
            }

            if (command == SelectionCommand.Cancel &&
                (_screenshotCompleting || annotations.IsBusy || annotations.IsDrawing))
            {
                _autoConfirmPending = false;
                _screenshotWork?.Cancel();
                annotations.CancelWork();
                return true;
            }

            if (annotations.IsBusy) return true;
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (command)
            {
                case SelectionCommand.Clear when annotations.IsDrawing:
                    annotations.CancelGesture();
                    return true;
                case SelectionCommand.Cancel when annotations.HasSelectedAnnotation:
                    annotations.ClearAnnotationSelection();
                    return true;
                case SelectionCommand.Clear when annotations.HasSelectedAnnotation:
                    annotations.DeleteSelectedAnnotation();
                    return true;
                case SelectionCommand.Move when annotations.HasSelectedAnnotation:
                    var dx = definition.DirectionKey switch { Key.Left => -1, Key.Right => 1, _ => 0 };
                    var dy = definition.DirectionKey switch { Key.Up => -1, Key.Down => 1, _ => 0 };
                    _ = annotations.MoveSelectedAnnotationAsync(dx, dy);
                    return true;
                case SelectionCommand.Cancel when annotations.Tool != ScreenshotTool.Select:
                    annotations.Tool = ScreenshotTool.Select;
                    return true;
                default:
                    return false;
            }
        }

        private void CompleteSelection(SelectionResult? result)
        {
            if (!ScreenshotMode || result is null)
            {
                Finish(result);
                return;
            }

            if (_screenshotCompleting || Annotations is not { IsBusy: false, IsDrawing: false }) return;
            if (NeedsWindowScreenshot)
            {
                _selectedTarget = result.Target;
                _state.Set(result.Region);
                _pendingFinish = false;
                _pendingResult = null;
                PrepareWindowScreenshot();
                return;
            }

            _pendingFinish = false;
            _pendingResult = null;
            _autoConfirmPending = false;
            _screenshotTask = CompleteScreenshotAsync(result);
        }

        private async Task CompleteScreenshotAsync(SelectionResult result)
        {
            var annotations = Annotations!;
            var decoration = Decoration;
            var outputOrigin = ScreenshotOutputOrigin;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _screenshotWork = work;
            _screenshotCompleting = true;
            annotations.SetExportState(true);
            Refresh();
            try
            {
                var token = work.Token;
                var png = await ComposeSelectedScreenshotAsync(result, annotations, token);

                if (_finished) return;
                var (hdr, hdrReason) = await ComposeSelectedHdrScreenshotAsync(result, annotations, token);
                var completed = new ScreenshotResult(png, ConfirmAction, hdr, hdrReason)
                    { ColorMode = annotations.OutputColorMode, Decoration = decoration, OutputOrigin = outputOrigin };
                if (ScreenshotOutputHandler is { } output)
                {
                    foreach (var window in _windows) window.Topmost = false;
                    if (!await output(ScreenshotOwner, completed, token)) return;
                    token.ThrowIfCancellationRequested();
                    if (_finished) return;
                }

                Finish(result);
            }
            catch (OperationCanceledException) when (_finished || work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_finished) annotations.SetExportState(false, error.Message);
            }
            finally
            {
                _screenshotCompleting = false;
                if (ReferenceEquals(_screenshotWork, work)) _screenshotWork = null;
                if (!_finished)
                {
                    if (annotations.IsBusy) annotations.SetExportState(false);
                    foreach (var window in _windows) window.Topmost = true;
                    ScreenshotOwner?.Activate();
                    Refresh();
                }
            }
        }
    }
}
