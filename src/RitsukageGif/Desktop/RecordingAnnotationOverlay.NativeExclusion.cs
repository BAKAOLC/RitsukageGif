using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingAnnotationOverlay
    {
        private readonly Dictionary<Window, LinuxCaptureExclusionLease> _nativeLeases = [];
        private readonly HashSet<Window> _nativeProtected = [];
        private readonly List<Task> _nativeReleaseTasks = [];
        private readonly List<Task> _nativeAuxiliaryPreparations = [];
        private CancellationTokenSource? _nativeLifetime;
        private Task<LinuxCaptureExclusionResult>? _nativePreparation;
        private LinuxCaptureExclusionProof? _nativeProof;
        private Task? _nativeRelease;

        internal LinuxCaptureExclusionFailure NativeReleaseFailure { get; private set; }
        internal Exception? NativePreparationFailure { get; private set; }
        internal bool NeedsNativeWindowPreparation => OperatingSystem.IsLinux() && _nativeProof is not null;
        internal event Action<LinuxCaptureExclusionResult>? NativeExclusionFailed;

        internal bool OwnsNativeExclusionWindow(TopLevel? window)
        {
            return window is Window owner && _nativeProtected.Contains(owner);
        }

        internal async Task BindHostedSourceExclusionAsync(LinuxCaptureExclusionProof proof,
            CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            token.ThrowIfCancellationRequested();
            if (_nativeRelease is { } previous)
            {
                await previous;
                _nativeRelease = null;
                NativePreparationFailure = null;
                NativeReleaseFailure = LinuxCaptureExclusionFailure.None;
            }

            _nativeProof = proof;
            _nativeLifetime ??= CancellationTokenSource.CreateLinkedTokenSource(token);
        }

        internal async Task<LinuxCaptureExclusionResult> PrepareAuxiliaryWindowAsync(Window window, string marker)
        {
            Dispatcher.UIThread.VerifyAccess();
            var task = PrepareAuxiliaryCoreAsync(window, marker);
            _nativeAuxiliaryPreparations.Add(task);
            return await task;
        }

        private async Task<LinuxCaptureExclusionResult> PrepareAuxiliaryCoreAsync(Window window, string marker)
        {
            var result = await LinuxCaptureExclusionLease.CreateAsync(_nativeProof!.Value, marker,
                _nativeLifetime!.Token);
            if (result.Lease is { } acquired) _nativeLeases.Add(window, acquired);
            _nativeLifetime.Token.ThrowIfCancellationRequested();
            if (!window.IsVisible)
            {
                if (_nativeLeases.Remove(window, out var closed)) QueueNativeRelease(closed);
                return result;
            }

            if (result.Capability == LinuxCaptureExclusionCapability.Unavailable)
            {
                NativeExclusionFailed?.Invoke(result);
                return result;
            }

            _nativeProtected.Add(window);
            window.Closed += (_, _) =>
            {
                _nativeProtected.Remove(window);
                if (_nativeLeases.Remove(window, out var lease)) QueueNativeRelease(lease);
            };
            return result;
        }

        internal async Task<LinuxCaptureExclusionResult> PrepareNativeExclusionAsync(LinuxCaptureExclusionProof proof,
            CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            token.ThrowIfCancellationRequested();
            if (_nativeRelease is { } previous)
            {
                await previous;
                _nativeRelease = null;
                NativePreparationFailure = null;
                NativeReleaseFailure = LinuxCaptureExclusionFailure.None;
            }

            if (_nativeProof == proof && _nativePreparation is { IsCompleted: false } running) return await running;
            _nativeProof = proof;
            _nativeLifetime ??= CancellationTokenSource.CreateLinkedTokenSource(token);
            _nativePreparation = PrepareNativeWindowsAsync(proof, _nativeLifetime.Token);
            return await _nativePreparation;
        }

        private async Task<LinuxCaptureExclusionResult> PrepareNativeWindowsAsync(LinuxCaptureExclusionProof proof,
            CancellationToken token)
        {
            try
            {
                return await PrepareNativeWindowsCoreAsync(proof, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                NativePreparationFailure = error;
                StopNativeExposure();
                NativeExclusionFailed?.Invoke(new(LinuxCaptureExclusionCapability.Unavailable,
                    LinuxCaptureExclusionFailure.ExclusionNotAcknowledged));
                throw;
            }
        }

        private async Task<LinuxCaptureExclusionResult> PrepareNativeWindowsCoreAsync(LinuxCaptureExclusionProof proof,
            CancellationToken token)
        {
            if (proof.Source != LinuxCaptureExclusionSource.SelectedWindow &&
                (proof.Source != LinuxCaptureExclusionSource.KWinPortal || proof.ProducerPid == 0))
            {
                var unavailable = new LinuxCaptureExclusionResult(LinuxCaptureExclusionCapability.Unavailable,
                    LinuxCaptureExclusionFailure.SourceDoesNotSupportExclusion);
                StopNativeExposure();
                NativeExclusionFailed?.Invoke(unavailable);
                return unavailable;
            }

            if (_windows.Count == 0 && proof.Source != LinuxCaptureExclusionSource.SelectedWindow)
                return new(LinuxCaptureExclusionCapability.Unavailable,
                    LinuxCaptureExclusionFailure.ExclusionNotAcknowledged);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                foreach (var (window, _) in _windows.ToArray())
                {
                    if (_nativeProtected.Contains(window)) continue;
                    var result = await LinuxCaptureExclusionLease.CreateAsync(proof, window.Title!, token);
                    if (result.Lease is { } acquired) _nativeLeases.Add(window, acquired);
                    token.ThrowIfCancellationRequested();
                    if (result.Capability == LinuxCaptureExclusionCapability.Unavailable)
                    {
                        StopNativeExposure();
                        NativeExclusionFailed?.Invoke(result);
                        return result;
                    }

                    if (_windows.All(item => item.Window != window))
                    {
                        if (_nativeLeases.Remove(window, out var obsolete)) QueueNativeRelease(obsolete);
                        continue;
                    }

                    _nativeProtected.Add(window);
                }

                if (_windows.All(item => _nativeProtected.Contains(item.Window))) break;
            }

            if (_visible)
                foreach (var (window, surface) in _windows)
                    if (_nativeProtected.Contains(window))
                    {
                        window.Content = surface;
                        window.Opacity = 1;
                    }

            UpdateInput(true);
            return new(proof.Source == LinuxCaptureExclusionSource.SelectedWindow
                ? LinuxCaptureExclusionCapability.SelectedWindowIsolated
                : LinuxCaptureExclusionCapability.NativeCompositorExclusion);
        }

        private void ResumeNativeExclusion()
        {
            if (_nativeProof is not { } proof || _nativeLifetime is not { IsCancellationRequested: false } lifetime ||
                _nativePreparation is { IsCompleted: false }) return;
            _nativePreparation = PrepareNativeWindowsAsync(proof, lifetime.Token);
        }

        private void StopNativeExposure()
        {
            foreach (var window in _nativeProtected.Concat(_nativeLeases.Keys)
                         .Where(window => _windows.All(item => item.Window != window)).Distinct().ToArray())
            {
                window.Opacity = 0;
                window.Close(null);
            }

            foreach (var (window, _) in _windows)
            {
                window.Content = null;
                window.Opacity = 0;
            }

            UpdateInput(true);
        }

        private void QueueNativeRelease(LinuxCaptureExclusionLease lease)
        {
            _nativeReleaseTasks.Add(ReleaseLeaseAsync(lease));
        }

        private async Task ReleaseLeaseAsync(LinuxCaptureExclusionLease lease)
        {
            await lease.DisposeAsync();
            if (lease.ReleaseFailure != LinuxCaptureExclusionFailure.None) NativeReleaseFailure = lease.ReleaseFailure;
        }

        internal ValueTask ReleaseNativeExclusionAsync()
        {
            Dispatcher.UIThread.VerifyAccess();
            return new(_nativeRelease ??= ReleaseNativeCoreAsync());
        }

        private async Task ReleaseNativeCoreAsync()
        {
            StopNativeExposure();
            _nativeProof = null;
            if (_nativeLifetime is { } lifetime) await lifetime.CancelAsync();
            if (_nativePreparation is { } pending)
                try
                {
                    await pending;
                }
                catch (OperationCanceledException) when (_nativeLifetime?.IsCancellationRequested == true)
                {
                }
                catch (Exception error)
                {
                    NativePreparationFailure = error;
                }

            try
            {
                await Task.WhenAll(_nativeAuxiliaryPreparations);
            }
            catch (OperationCanceledException) when (_nativeLifetime?.IsCancellationRequested == true)
            {
            }
            catch (Exception error)
            {
                NativePreparationFailure = error;
            }

            _nativeAuxiliaryPreparations.Clear();
            foreach (var lease in _nativeLeases.Values) QueueNativeRelease(lease);
            _nativeLeases.Clear();
            _nativeProtected.Clear();
            try
            {
                await Task.WhenAll(_nativeReleaseTasks);
            }
            finally
            {
                _nativeReleaseTasks.Clear();
                _nativePreparation = null;
                _nativeLifetime?.Dispose();
                _nativeLifetime = null;
            }
        }
    }
}
