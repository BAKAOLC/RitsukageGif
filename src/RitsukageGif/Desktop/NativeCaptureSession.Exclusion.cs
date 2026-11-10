using System.Runtime.ExceptionServices;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class NativeCaptureSession
    {
        private readonly TaskCompletionSource<LinuxCaptureExclusionProof> _exclusionProofReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void ObserveExclusionProof()
        {
            switch (_capabilities)
            {
                case { Window: true }:
                    _exclusionProofReady.TrySetResult(new(LinuxCaptureExclusionSource.SelectedWindow));
                    break;
                case { ProducerChecked: true } capabilities:
                    _exclusionProofReady.TrySetResult(capabilities is { Producer: "kwin", ProducerPid: > 0 }
                        ? new(LinuxCaptureExclusionSource.KWinPortal, capabilities.ProducerPid)
                        : new(LinuxCaptureExclusionSource.OtherPortal));
                    break;
            }
        }

        internal async Task<LinuxCaptureExclusionProof> ReadExclusionProofAsync(CancellationToken token)
        {
            CancellationToken sourceToken;
            lock (_gate)
            {
                if (_disposed) throw new CaptureSourceLostException();
                sourceToken = _stop.Token;
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, sourceToken);
            LinuxCaptureExclusionProof proof;
            try
            {
                proof = await _exclusionProofReady.Task.WaitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested &&
                                                     sourceToken.IsCancellationRequested)
            {
                throw new CaptureSourceLostException();
            }

            lock (_gate)
            {
                if (_disposed) throw new CaptureSourceLostException();
                if (_failure is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
                return proof;
            }
        }
    }
}
