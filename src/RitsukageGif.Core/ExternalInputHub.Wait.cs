namespace RitsukageGif.Core
{
    public sealed partial class ExternalInputHub
    {
        public async Task<InputChannelData?> WaitAsync(string name, long afterVersion, double timeoutSeconds,
            CancellationToken token = default)
        {
            ValidateName(name);
            ArgumentOutOfRangeException.ThrowIfLessThan(afterVersion, -1);
            if (!double.IsFinite(timeoutSeconds) || timeoutSeconds is < 0 or > 86400)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            var deadline = Now + timeoutSeconds;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                Task changed;
                lock (_gate)
                {
                    if (_disposed) return null;
                    if (_channels.TryGetValue(name, out var channel) && channel.DataVersion > afterVersion)
                        return new(channel.Json, channel.DataVersion, channel.DataTimestamp,
                            channel.Connected);
                    changed = _changed.Task;
                }

                var remaining = deadline - Now;
                if (remaining <= 0) return null;
                try
                {
                    await changed.WaitAsync(TimeSpan.FromSeconds(remaining), token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    return null;
                }
            }
        }

        // Call while holding _gate: a waiter either sees the new version or subscribes to the
        // next broadcast under the same lock, so publication cannot fall between check and wait.
        private void Signal()
        {
            var previous = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            previous.TrySetResult(true);
        }
    }
}
