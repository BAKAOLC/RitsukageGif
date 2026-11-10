using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecordingCameraPreviewOverlay
    {
        private readonly LinuxCaptureExclusionProof? _captureProof;
        private readonly Dictionary<Window, LinuxCaptureExclusionLease> _nativeLeases = [];
        private readonly Dictionary<Window, Task> _nativePreparations = [];
        private readonly CancellationTokenSource _nativeLifetime = new();

        private bool PrepareNativeWindow(Window window)
        {
            if (_nativeLeases.ContainsKey(window)) return true;
            if (_nativePreparations.ContainsKey(window)) return false;
            if (_captureProof is not { Source: LinuxCaptureExclusionSource.KWinPortal } proof)
                throw new NotSupportedException("The selected source cannot exclude camera preview windows.");
            var marker = LinuxCaptureExclusionLease.CreateWindowMarker();
            var content = window.Content;
            window.Content = null;
            window.Opacity = 0;
            window.Title = marker;
            window.Show();
            _nativePreparations.Add(window, ExposeNativeWindowAsync(window, marker, content, proof));
            return false;
        }

        private async Task ExposeNativeWindowAsync(Window window, string marker, object? content,
            LinuxCaptureExclusionProof proof)
        {
            try
            {
                var result = await LinuxCaptureExclusionLease.CreateAsync(proof, marker, _nativeLifetime.Token);
                if (result.Lease is not { } lease)
                    throw new NotSupportedException($"Camera preview exclusion is unavailable: {result.Failure}.");
                _nativeLeases.Add(window, lease);
                if (Volatile.Read(ref _disposed) != 0) return;
                window.Hide();
                window.Title = "";
                window.Content = content;
                window.Opacity = 1;
                QueueFrame();
            }
            catch (OperationCanceledException) when (_nativeLifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                window.Hide();
                if (Volatile.Read(ref _disposed) == 0) Failed?.Invoke(error.Message);
            }
        }

        private async Task ReleaseNativeWindowsAsync()
        {
            await _nativeLifetime.CancelAsync();
            await Task.WhenAll(_nativePreparations.Values);
            foreach (var lease in _nativeLeases.Values) await lease.DisposeAsync();
            _nativeLeases.Clear();
            _nativePreparations.Clear();
            _nativeLifetime.Dispose();
        }

        private async Task CloseNativeWindowsAsync()
        {
            CloseWindows();
            await ReleaseNativeWindowsAsync();
        }
    }
}
