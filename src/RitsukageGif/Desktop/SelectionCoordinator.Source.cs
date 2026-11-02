namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private async Task SwitchScreenshotSourceAsync(bool windowSource)
        {
            if (await SwitchScreenshotSourceCoreAsync(windowSource, false)) PreferencesChanged?.Invoke();
        }

        private async Task<bool> SwitchScreenshotSourceCoreAsync(bool windowSource, bool forceRefresh)
        {
            var annotations = Annotations!;
            var target = Snapshot.Target;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _screenshotWork = work;
            _screenshotCompleting = true;
            annotations.SetExportState(true);
            FrozenWindowScreenshot? created = null;
            var committed = false;
            try
            {
                var next = !forceRefresh && windowSource && _frozenWindow?.Target.Handle == target?.Handle &&
                           (ShapeMode != SelectionShapeMode.MultiWindow || _frozenShapeRevision == _windowShapeRevision)
                    ? _frozenWindow
                    : null;
                if (windowSource && next is null)
                {
                    if (target is null) throw new CaptureSourceLostException();
                    var targets = ShapeMode == SelectionShapeMode.MultiWindow && _selectionWindows.Count > 1
                        ? SelectedShapeWindows()
                        : [target];
                    var region = targets.Length > 1 ? _state.Selected : target.Client;
                    var pixels = await Task.Run(() => CaptureSelectedWindows(targets, region, work.Token), work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    created = next = new(target, region, pixels.Pixels, pixels.Hdr, pixels.NativeFrames);
                }

                await annotations.SetOverlayFrameAsync(next?.Region ?? default, next?.Pixels, work.Token);
                if (created is not null)
                {
                    _frozenWindow?.Dispose();
                    _frozenWindow = created;
                    _frozenShapeRevision = _windowShapeRevision;
                    created = null;
                }

                WindowSource = windowSource;
                committed = true;
                UpdatePoint();
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_finished) annotations.SetExportState(false, error.Message);
            }
            finally
            {
                created?.Dispose();
                _screenshotCompleting = false;
                if (ReferenceEquals(_screenshotWork, work)) _screenshotWork = null;
                if (!_finished)
                {
                    if (annotations.IsBusy) annotations.SetExportState(false);
                    Refresh();
                }
            }

            return committed;
        }
    }
}
