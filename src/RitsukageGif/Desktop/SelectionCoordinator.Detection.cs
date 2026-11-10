namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private SelectionWindowMonitor? _windowMonitor;
        private Task? _candidateRefreshTask;
        private bool _candidatesInitialized;
        private bool _candidatesDirty;
        private bool _candidateRefreshPending;
        private WindowTarget? _observedDetectionTarget;

        private WindowTarget? CandidateAtPoint(PixelPoint point)
        {
            return ObserveDetectionTarget(WindowLocator.CandidateAtPoint(_targets, point));
        }

        private WindowTarget? ObserveDetectionTarget(WindowTarget? target)
        {
            if (_observedDetectionTarget is { } previous &&
                (target is null || previous.Handle != target.Handle || previous.ProcessId != target.ProcessId ||
                 previous.Bounds != target.Bounds || previous.Client != target.Client)) InvalidateControlDetection();
            _observedDetectionTarget = target;
            return target;
        }

        private void CandidatesChanged()
        {
            if (_finished) return;
            _candidatesDirty = true;
            InvalidateControlDetection();
            if (_candidatesInitialized && !_candidateRefreshPending)
                _candidateRefreshTask = RefreshCandidatesAsync();
        }

        private async Task RefreshCandidatesAsync()
        {
            _candidateRefreshPending = true;
            try
            {
                while (!_finished && _candidatesDirty)
                {
                    _candidatesDirty = false;
                    var candidates = await Task.Run(WindowLocator.ListCandidates, _cancel.Token);
                    if (_finished) return;
                    if (_candidatesDirty) continue;
                    _targets = candidates;
                    UpdatePoint();
                    Refresh();
                }
            }
            catch (OperationCanceledException) when (_finished)
            {
            }
            catch (Exception error)
            {
                SelectionLogger.ZLogWarning(error,
                    $"Selection window inventory refresh failed. SelectionId={_selectionId}");
            }
            finally
            {
                _candidateRefreshPending = false;
            }
        }
    }
}
