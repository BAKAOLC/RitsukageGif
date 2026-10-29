using Microsoft.Extensions.Logging;

namespace RitsukageGif.Diagnostics
{
    internal sealed class DeferredDiagnosticLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            var backend = LogHub.Backend;
            if (backend is null || !backend.TryEnter()) return null;
            try
            {
                return backend.GetLogger(category).BeginScope(state);
            }
            catch (Exception error)
            {
                LogHub.ReportSinkFailure(error);
                return null;
            }
            finally
            {
                backend.Exit();
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return LogHub.Backend is { } backend && backend.IsEnabled(category, logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var backend = LogHub.Backend;
            if (backend is null || !backend.IsEnabled(category, logLevel) || !backend.TryEnter()) return;
            try
            {
                if (!backend.Budget.TryAcquire(logLevel))
                {
                    LogHub.ReportDrop(logLevel);
                    return;
                }

                try
                {
                    backend.GetLogger(category).Log(logLevel, eventId, state, exception, formatter);
                }
                catch (Exception error)
                {
                    backend.Budget.Release();
                    LogHub.ReportSinkFailure(error);
                }
            }
            finally
            {
                backend.Exit();
            }
        }
    }
}
