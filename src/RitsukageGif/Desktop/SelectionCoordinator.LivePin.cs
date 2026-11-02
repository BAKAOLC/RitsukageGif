namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal Func<WindowTarget, PixelRect, bool, double, CancellationToken, Task>? WindowPinOutputHandler
        {
            get;
            set;
        }

        private WindowTarget? LivePinTarget
        {
            get
            {
                var region = Snapshot.Region;
                if (UsesLocalSource || ScreenshotPreview || region.IsEmpty || ShapeMode != SelectionShapeMode.Rectangle)
                    return null;
                var center = new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
                var target = Snapshot.Target ?? _targets.FirstOrDefault(candidate => candidate.Bounds.Contains(center));
                var bounds = target is null ? default : OperatingSystem.IsLinux() ? target.Client : target.Bounds;
                return target is not null && !region.Intersect(bounds).IsEmpty ? target : null;
            }
        }

        private void RequestWindowPin()
        {
            if (_finished || _screenshotCompleting || WindowPinOutputHandler is null ||
                Annotations is not { CanPinWindow: true } || LivePinTarget is not { } target) return;
            _screenshotTask = CompleteWindowPinAsync(target, Snapshot.Region, Snapshot.Cursor);
        }

        private async Task CompleteWindowPinAsync(WindowTarget target, PixelRect region, bool cursor)
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _screenshotWork = work;
            _screenshotCompleting = true;
            var annotations = Annotations!;
            annotations.SetExportState(true);
            Refresh();
            try
            {
                foreach (var window in _windows) window.Topmost = false;
                await WindowPinOutputHandler!(target, region, cursor, ScreenshotCaptureScaling, work.Token);
                work.Token.ThrowIfCancellationRequested();
                if (!_finished) Finish(new(region, true, target, cursor));
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
