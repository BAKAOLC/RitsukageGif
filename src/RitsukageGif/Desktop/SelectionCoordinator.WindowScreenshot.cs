namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private FrozenWindowScreenshot? _frozenWindow;
        private int _windowShapeRevision, _frozenShapeRevision;

        internal FrozenWindowScreenshot? FrozenWindow => WindowSource && !UsesLocalSource &&
                                                         _frozenWindow?.Target.Handle == Snapshot.Target?.Handle
            ? _frozenWindow
            : null;

        private bool NeedsWindowScreenshot => ScreenshotMode && WindowSource && !UsesLocalSource &&
                                              Snapshot.Target is { } target &&
                                              (_frozenWindow?.Target.Handle != target.Handle ||
                                               (ShapeMode == SelectionShapeMode.MultiWindow &&
                                                _frozenShapeRevision != _windowShapeRevision));

        private void PrepareWindowScreenshot()
        {
            if (!NeedsWindowScreenshot || _screenshotCompleting || _state.Dragging || _state.Selected.IsEmpty) return;
            var target = Snapshot.Target!;
            _screenshotTask = FreezeWindowScreenshotAsync(target);
        }

        private void UpdateWindowOverlayFrame()
        {
            UpdateHdrAvailability();
            if (Annotations is { } annotations)
            {
                annotations.RecordingEnabled = AllowRecording && !ScreenshotPreview;
                annotations.WindowPinEnabled = WindowPinOutputHandler is not null && LivePinTarget is not null;
                annotations.CanAnnotate = !NeedsWindowScreenshot;
                if (!annotations.CanAnnotate && annotations.Tool != ScreenshotTool.Select)
                    annotations.Tool = ScreenshotTool.Select;
            }
        }

        private async Task FreezeWindowScreenshotAsync(WindowTarget target)
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _screenshotWork = work;
            _screenshotCompleting = true;
            Annotations!.SetExportState(true);
            Refresh();
            try
            {
                var targets = ShapeMode == SelectionShapeMode.MultiWindow && _selectionWindows.Count > 1
                    ? SelectedShapeWindows()
                    : [target];
                var revision = _windowShapeRevision;
                var region = targets.Length > 1 ? _state.Selected : target.Client;
                var pixels = await Task.Run(() => CaptureSelectedWindows(targets, region, work.Token), work.Token);
                work.Token.ThrowIfCancellationRequested();
                var next = new FrozenWindowScreenshot(target, region, pixels.Pixels, pixels.Hdr, pixels.NativeFrames);
                try
                {
                    await Annotations.SetOverlayFrameAsync(region, pixels.Pixels, work.Token);
                }
                catch
                {
                    next.Dispose();
                    throw;
                }

                _frozenWindow?.Dispose();
                _frozenWindow = next;
                _frozenShapeRevision = revision;
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_finished) Annotations.SetExportState(false, error.Message);
            }
            finally
            {
                _screenshotCompleting = false;
                if (ReferenceEquals(_screenshotWork, work)) _screenshotWork = null;
                if (!_finished)
                {
                    if (Annotations.IsBusy) Annotations.SetExportState(false);
                    Refresh();
                    ResumeAutoConfirm();
                }
            }
        }
    }
}
