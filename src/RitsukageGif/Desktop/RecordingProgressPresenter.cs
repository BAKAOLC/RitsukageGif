using System.Diagnostics;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingProgressPresenter(Action<RecordingProgress> update)
        : IProgress<RecordingProgress>, IDisposable
    {
        private int _completed, _queued;
        private long _lastUpdate;
        private RecordingProgress? _latest;
        private IDisposable? _scheduled;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0) return;
            _scheduled?.Dispose();
            _scheduled = null;
            if (Volatile.Read(ref _latest) is { } value) update(value);
        }

        public void Report(RecordingProgress value)
        {
            if (Volatile.Read(ref _completed) != 0) return;
            Volatile.Write(ref _latest, value);
            if (Interlocked.Exchange(ref _queued, 1) == 0) Dispatcher.UIThread.Post(Flush);
        }

        private void Flush()
        {
            _scheduled = null;
            if (Volatile.Read(ref _completed) != 0) return;
            var remaining = 100 - Stopwatch.GetElapsedTime(_lastUpdate).TotalMilliseconds;
            if (_lastUpdate != 0 && remaining > 0 && Volatile.Read(ref _latest)?.Stage != RecordingStage.Complete)
            {
                _scheduled = DispatcherTimer.RunOnce(Flush, TimeSpan.FromMilliseconds(remaining));
                return;
            }

            Interlocked.Exchange(ref _queued, 0);
            if (Volatile.Read(ref _latest) is not { } value) return;
            _lastUpdate = Stopwatch.GetTimestamp();
            update(value);
        }
    }
}
