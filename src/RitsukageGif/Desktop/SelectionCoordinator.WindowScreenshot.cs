namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private FrozenWindowScreenshot? _frozenWindow;

        internal FrozenWindowScreenshot? FrozenWindow => WindowSource && !UsesLocalSource &&
                                                         _frozenWindow?.Target.Handle == Snapshot.Target?.Handle
            ? _frozenWindow
            : null;

        private bool NeedsWindowScreenshot => ScreenshotMode && WindowSource && !UsesLocalSource &&
                                              Snapshot.Target is { } target &&
                                              _frozenWindow?.Target.Handle != target.Handle;

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
                var pixels = await Task.Run(() => CaptureWindowPixels(new(target.Client, true, target,
                    !OperatingSystem.IsWindows() && _cursor), work.Token), work.Token);
                work.Token.ThrowIfCancellationRequested();
                var next = new FrozenWindowScreenshot(target, target.Client, pixels.Pixels, pixels.Hdr);
                try
                {
                    await Annotations.SetOverlayFrameAsync(target.Client, pixels.Pixels, work.Token);
                }
                catch
                {
                    next.Dispose();
                    throw;
                }

                _frozenWindow?.Dispose();
                _frozenWindow = next;
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
