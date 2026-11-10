using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PortableCaptureInputSubscription : IAsyncDisposable
    {
        private readonly Action _released;
        private readonly Action _cancelled;
        private readonly Action<Exception> _failed;
        private readonly CancellationToken _token;
        private readonly RecordingInputObserver _observer;
        private Task _completion = Task.CompletedTask;
        private int _terminal;
        private int _disposed;

        private PortableCaptureInputSubscription(Action released, Action cancelled, Action<Exception> failed,
            CancellationToken token)
        {
            _released = released;
            _cancelled = cancelled;
            _failed = failed;
            _token = token;
            _observer = new(true, true, OnInput);
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            try
            {
                await _observer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                LogHub.GetLogger("Desktop.Capture.Input").ZLogDebug(error,
                    $"Capture input observer stopped with an error.");
            }

            await _completion.ConfigureAwait(false);
        }

        internal static async Task<IAsyncDisposable> StartAsync(Action released, Action cancelled,
            Action<Exception> failed, CancellationToken token)
        {
            var subscription = new PortableCaptureInputSubscription(released, cancelled, failed, token);
            try
            {
                await subscription._observer.StartAsync(token).ConfigureAwait(false);
                subscription._completion = subscription.ObserveCompletionAsync();
                var state = await Task.Run(ReadInitialState, token).ConfigureAwait(false);
                if (state.Cancelled) subscription.Signal(cancelled);
                else if (!state.Held) subscription.Signal(released);
                token.ThrowIfCancellationRequested();
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private void OnInput(RecordingInputEvent input)
        {
            switch (input)
            {
                case { Kind: RecordingInputEventKind.PointerUp, Button: 1 }:
                    Signal(_released);
                    break;
                case { Kind: RecordingInputEventKind.KeyDown, Key: Key.Escape }:
                    Signal(_cancelled);
                    break;
            }
        }

        private async Task ObserveCompletionAsync()
        {
            try
            {
                await _observer.Completion.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Signal(() => _failed(error));
            }
        }

        private void Signal(Action callback)
        {
            if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (Volatile.Read(ref _disposed) == 0 && !_token.IsCancellationRequested) callback();
            });
        }
    }
}
