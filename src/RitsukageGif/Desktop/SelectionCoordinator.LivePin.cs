using System.Runtime.ExceptionServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal Func<WindowTarget, PixelRect, bool, double, CancellationToken, Task>? WindowPinOutputHandler
        {
            get;
            set;
        }

        private bool CanSelectLivePinSource => !UsesLocalSource && !ScreenshotPreview &&
                                               ShapeMode == SelectionShapeMode.Rectangle;

        private void RequestWindowPin()
        {
            if (_finished || _screenshotCompleting || WindowPinOutputHandler is null || !CanSelectLivePinSource ||
                Annotations is not { CanPinWindow: true }) return;
            var snapshot = Snapshot;
            if (snapshot.Region.IsEmpty) return;
            _screenshotTask = CompleteWindowPinAsync(snapshot.Region, snapshot.Cursor);
        }

        private async Task CompleteWindowPinAsync(PixelRect region, bool cursor)
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _screenshotWork = work;
            _screenshotCompleting = true;
            var annotations = Annotations!;
            annotations.SetExportState(true);
            Refresh();
            try
            {
                _showing = true;
                ReleaseCaptures();
                _replacingOverlays = true;
                try
                {
                    if (CloseOverlays() is { } closeError)
                        ExceptionDispatchInfo.Throw(closeError);
                }
                finally
                {
                    _replacingOverlays = false;
                }

                await CaptureWindowVisibility.FlushAsync(work.Token);
                var center = new PixelPoint((int)((2L * region.X + region.Width - 1) / 2),
                    (int)((2L * region.Y + region.Height - 1) / 2));
                var target = await Task.Run(() => WindowLocator.FindLivePinAtPoint(center), work.Token) ??
                             throw new CaptureSourceLostException();
                work.Token.ThrowIfCancellationRequested();
                var bounds = OperatingSystem.IsLinux() ? target.Client : target.Bounds;
                var clipped = region.Intersect(bounds);
                if (clipped.IsEmpty) throw new CaptureSourceLostException();
                await WindowPinOutputHandler!(target, clipped, cursor, ScreenshotCaptureScaling, work.Token);
                work.Token.ThrowIfCancellationRequested();
                if (!_finished) Finish(new(clipped, true, target, cursor));
            }
            catch (OperationCanceledException) when (_finished || work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (_finished) return;
                if (PropagateOutputErrors)
                {
                    Finish(null, error);
                }
                else
                {
                    SelectionLogger.ZLogWarning(error,
                        $"Window pin failed after screenshot handoff. SelectionId={_selectionId}");
                    CompletionNotice = error.Message;
                    Finish(null);
                }
            }
            finally
            {
                _screenshotCompleting = false;
                if (ReferenceEquals(_screenshotWork, work)) _screenshotWork = null;

                _showing = false;
            }
        }
    }
}
