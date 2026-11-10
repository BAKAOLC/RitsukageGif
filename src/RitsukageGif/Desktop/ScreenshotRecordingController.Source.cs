namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingController
    {
        private NativeCaptureSessionReference? _selectedSource;
        private Task _sourcePreparation = Task.CompletedTask;
        internal bool IsPreparingSource { get; private set; }

        internal long SourceRevision { get; private set; }
        internal bool SourceIsWindowIsolated { get; private set; }
        internal Func<LinuxCaptureExclusionProof, CancellationToken, Task>? PrepareSourceExclusion { get; set; }
        internal Func<Task>? BeforeSourceRelease { get; set; }

        private Task ReleaseRecorderExclusionAsync()
        {
            return BeforeSourceRelease?.Invoke() ?? Task.CompletedTask;
        }

        internal void ReportSourcePreviewFailure(Exception error)
        {
            Error = ApplicationHost.VideoErrorMessage(error);
            Logger.ZLogError(error, $"Recording source preview failed.");
            Notify();
        }

        private void SelectSource(SelectionResult selection)
        {
            var retained = selection.CaptureSession?.Retain();
            var previous = _selectedSource;
            _selectedSource = retained;
            Selection = selection with { CaptureSession = retained?.Session };
            SourceIsWindowIsolated = false;
            SourceRevision++;
            previous?.Dispose();
        }

        internal Task PrepareSelectedSourceAsync()
        {
            if (!OperatingSystem.IsLinux()) return Task.CompletedTask;
            _sourcePreparation = PrepareSelectedSourceCoreAsync(_selectedSource?.Session, _lifetime.Token);
            return _sourcePreparation;
        }

        private async Task PrepareSelectedSourceCoreAsync(NativeCaptureSession? source, CancellationToken token)
        {
            IsPreparingSource = true;
            SourceIsWindowIsolated = false;
            Notify();
            try
            {
                var selection = Selection;
                var proof = source is not null
                    ? await source.ReadExclusionProofAsync(token)
                    : await Task.Run(() => WindowContentCapture.ProbeSelectionIsolation(selection, token), token);
                token.ThrowIfCancellationRequested();
                SourceIsWindowIsolated = proof.Source == LinuxCaptureExclusionSource.SelectedWindow;
                if (PrepareSourceExclusion is { } prepare) await prepare(proof, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Error = ApplicationHost.VideoErrorMessage(error);
                Logger.ZLogError(error, $"Recording capture source preparation failed.");
            }
            finally
            {
                IsPreparingSource = false;
                Notify();
            }
        }

        private void ReleaseSelectedSource()
        {
            var previous = _selectedSource;
            _selectedSource = null;
            Selection = Selection with { CaptureSession = null };
            previous?.Dispose();
        }
    }
}
