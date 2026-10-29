namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession
    {
        private readonly Lock _parameterWakeGate = new();
        private long _observedParameterWake;
        private TaskCompletionSource _parameterWake = NewParameterWake();
        private long _parameterWakeRevision;

        private static TaskCompletionSource NewParameterWake()
        {
            return new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void NotifyParameterWait()
        {
            TaskCompletionSource wake;
            lock (_parameterWakeGate)
            {
                _parameterWakeRevision++;
                wake = _parameterWake;
                _parameterWake = NewParameterWake();
            }

            wake.TrySetResult();
        }

        internal async ValueTask<double> WaitParametersBound(double revision, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Task wake;
            bool contextChanged;
            lock (_parameterWakeGate)
            {
                wake = _parameterWake.Task;
                contextChanged = _observedParameterWake != _parameterWakeRevision;
                _observedParameterWake = _parameterWakeRevision;
            }

            if (contextChanged || !_events.IsEmpty)
            {
                await PumpAsync(token);
                return Parameters.Revision;
            }

            using var pending = CancellationTokenSource.CreateLinkedTokenSource(token);
            var change = Parameters.WaitChangedAsync(revision, pending.Token).AsTask();
            try
            {
                await await Task.WhenAny(change, wake.WaitAsync(pending.Token));
                await PumpAsync(token);
                return Parameters.Revision;
            }
            finally
            {
                await pending.CancelAsync();
            }
        }
    }
}
