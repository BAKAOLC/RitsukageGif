namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private async Task SwitchScreenshotSourceAsync(bool windowSource)
        {
            var annotations = Annotations!;
            var target = Snapshot.Target;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _screenshotWork = work;
            _screenshotCompleting = true;
            annotations.SetExportState(true);
            FrozenWindowScreenshot? created = null;
            try
            {
                var next = windowSource && _frozenWindow?.Target.Handle == target?.Handle ? _frozenWindow : null;
                if (windowSource && next is null)
                {
                    if (target is null) throw new CaptureSourceLostException();
                    var pixels = await Task.Run(() => CaptureWindowPixels(new(target.Client, true, target,
                        !OperatingSystem.IsWindows() && _cursor), work.Token), work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    created = next = new(target, target.Client, pixels.Pixels, pixels.Hdr);
                }

                await annotations.SetOverlayFrameAsync(next?.Region ?? default, next?.Pixels, work.Token);
                if (created is not null)
                {
                    _frozenWindow?.Dispose();
                    _frozenWindow = created;
                    created = null;
                }

                WindowSource = windowSource;
                PreferencesChanged?.Invoke();
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
        }
    }
}
