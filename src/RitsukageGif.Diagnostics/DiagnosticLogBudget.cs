using Microsoft.Extensions.Logging;

namespace RitsukageGif.Diagnostics
{
    internal sealed class DiagnosticLogBudget(int capacity, int reservedErrors)
    {
        private int _pending;

        internal int Pending => Volatile.Read(ref _pending);

        internal bool TryAcquire(LogLevel level)
        {
            var limit = level >= LogLevel.Error ? capacity : capacity - reservedErrors;
            while (true)
            {
                var pending = Volatile.Read(ref _pending);
                if (pending >= limit) return false;
                if (Interlocked.CompareExchange(ref _pending, pending + 1, pending) == pending) return true;
            }
        }

        internal void Release()
        {
            Interlocked.Decrement(ref _pending);
        }
    }
}
