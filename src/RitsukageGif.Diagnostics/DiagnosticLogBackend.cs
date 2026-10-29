using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ZLogger;
using ZLogger.Formatters;
using ZLogger.Providers;

namespace RitsukageGif.Diagnostics
{
    internal sealed class DiagnosticLogBackend : IAsyncDisposable
    {
        private readonly KeyValuePair<string, LogLevel>[] _categoryLevels;
        private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.Ordinal);
        private readonly LogLevel _minimumLevel;
        private readonly DiagnosticsOptions _options;
        private readonly ZLoggerRollingFileLoggerProvider _provider;
        private readonly TaskCompletionSource _writersDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _currentFilePath;
        private int _retired;
        private int _writers;

        internal DiagnosticLogBackend(string directory, string session, string role, DiagnosticsOptions options)
        {
            Directory = directory;
            _options = options;
            Budget = new(options.QueueCapacity, options.ReservedErrorCapacity);
            _minimumLevel = options.MinimumLevel;
            _categoryLevels = [.. options.CategoryLevels.OrderByDescending(pair => pair.Key.Length)];
            DiagnosticRetention.Apply(directory, options);
            var settings = new ZLoggerRollingFileOptions
            {
                FilePathSelector = (time, index) =>
                {
                    var path = Path.Combine(directory,
                        $"rg-{Environment.ProcessId}-{session}-{role}-{time:yyyyMMdd}-{index:0000}.log");
                    Volatile.Write(ref _currentFilePath, path);
                    DiagnosticRetention.Apply(directory, options);
                    return path;
                },
                RollingInterval = RollingInterval.Day,
                RollingSizeKB = options.RollingSizeKiB,
                BackgroundBufferCapacity = options.QueueCapacity,
                FullMode = BackgroundBufferFullMode.Block,
                IncludeScopes = true,
                InternalErrorLogger = LogHub.ReportSinkFailure,
            };
            settings.UseFormatter(() =>
            {
                var formatter = new PlainTextZLoggerFormatter();
                formatter.SetPrefixFormatter($"{0:utc-longdate} [{1:short}] [{2}] ",
                    static (in template, in info) =>
                        template.Format(info.Timestamp, info.LogLevel, info.Category));
                formatter.SetExceptionFormatter(DiagnosticExceptionFormatter.Format);
                return new DiagnosticBudgetFormatter(formatter, Budget);
            });
            _provider = new(settings);
            _provider.SetScopeProvider(new LoggerExternalScopeProvider());
        }

        private string Directory { get; }
        internal DiagnosticLogBudget Budget { get; }
        internal string? CurrentFilePath => Volatile.Read(ref _currentFilePath);

        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _retired, 1);
            if (Volatile.Read(ref _writers) == 0) _writersDrained.TrySetResult();
            await _writersDrained.Task.ConfigureAwait(false);
            try
            {
                await _provider.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                DiagnosticRetention.Apply(Directory, _options);
            }
        }

        internal bool IsEnabled(string category, LogLevel level)
        {
            var minimum = _minimumLevel;
            foreach (var pair in _categoryLevels)
                if (category == pair.Key || (category.Length > pair.Key.Length &&
                                             category[pair.Key.Length] == '.' &&
                                             category.StartsWith(pair.Key, StringComparison.Ordinal)))
                {
                    minimum = pair.Value;
                    break;
                }

            return level != LogLevel.None && level >= minimum;
        }

        internal ILogger GetLogger(string category)
        {
            return _loggers.GetOrAdd(category, static (name, provider) => provider.CreateLogger(name), _provider);
        }

        internal bool TryEnter()
        {
            if (Volatile.Read(ref _retired) != 0) return false;
            Interlocked.Increment(ref _writers);
            if (Volatile.Read(ref _retired) == 0) return true;
            Exit();
            return false;
        }

        internal void Exit()
        {
            if (Interlocked.Decrement(ref _writers) == 0 && Volatile.Read(ref _retired) != 0)
                _writersDrained.TrySetResult();
        }
    }
}
