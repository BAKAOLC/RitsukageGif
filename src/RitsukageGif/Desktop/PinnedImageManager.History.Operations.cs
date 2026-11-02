using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        public Task<bool> RestoreLastClosedAsync(CancellationToken token = default)
        {
            return BeginHistoryRestore(token);
        }

        private Task<bool> TryRestoreMatchingLiveAsync(WindowRoiPinDescriptor descriptor, CancellationToken token)
        {
            return BeginHistoryRestore(token, descriptor);
        }

        private Task<bool> BeginHistoryRestore(CancellationToken token, WindowRoiPinDescriptor? descriptor = null)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var operation = RestoreHistoryAsync(descriptor, token);
            _historyOperations.Add(operation);
            _ = ObserveHistoryAsync(operation);
            return operation;
        }

        private async Task<bool> RestoreHistoryAsync(WindowRoiPinDescriptor? descriptor, CancellationToken token)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _historyCancellation.Token);
            return descriptor is null
                ? await RestoreLastClosedCoreAsync(cancellation.Token)
                : await RestoreMatchingLiveCoreAsync(descriptor, cancellation.Token);
        }

        private async Task ObserveHistoryAsync(Task operation)
        {
            try
            {
                await operation;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                PinLogger.ZLogWarning(
                    $"Closed pin restoration failed. ErrorType={error.GetType().Name} Code={error.HResult}");
            }
            finally
            {
                _historyOperations.Remove(operation);
            }
        }

        public async Task StopHistoryAsync()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            await _historyCancellation.CancelAsync();
            var operations = _historyOperations.ToArray();
            try
            {
                await Task.WhenAll(operations);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                PinLogger.ZLogWarning(
                    $"Pending pin history operations stopped. ErrorType={error.GetType().Name} Code={error.HResult}");
            }
        }
    }
}
