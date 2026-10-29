using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace RitsukageGif.Diagnostics
{
    public static class LogHub
    {
        private static readonly Lock Lifecycle = new();
        private static readonly ConcurrentDictionary<string, ILogger> Loggers = new(StringComparer.Ordinal);
        private static DiagnosticLogBackend? _backend;
        private static Task? _shutdown;
        private static string? _lastFilePath;
        private static long _droppedEntries;
        private static long _errorDrops;
        private static long _sinkFailures;
        private static int _reportedSinkFailure;
        private static int _reportedErrorDrop;

        internal static DiagnosticLogBackend? Backend => Volatile.Read(ref _backend);
        public static int PendingEntries => Backend?.Budget.Pending ?? 0;
        public static bool IsInitialized => Backend is not null;
        public static string? CurrentFilePath => Backend?.CurrentFilePath ?? Volatile.Read(ref _lastFilePath);
        public static long DroppedEntries => Interlocked.Read(ref _droppedEntries);
        public static long ErrorDrops => Interlocked.Read(ref _errorDrops);
        public static long SinkFailures => Interlocked.Read(ref _sinkFailures);

        public static ILogger GetLogger(string category)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(category);
            return Loggers.GetOrAdd(category, static name => new DeferredDiagnosticLogger(name));
        }

        public static void Initialize(string dataDirectory, string version, string role,
            DiagnosticsOptions? options = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
            ArgumentException.ThrowIfNullOrWhiteSpace(role);
            if (role.Length > 32 || role.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
                throw new ArgumentException("The log role must be a short ASCII identifier.", nameof(role));
            options ??= new();
            options.Validate();
            if (Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("RITSUKAGE_LOG_LEVEL"), true,
                    out var minimum) && Enum.IsDefined(minimum)) options = options with { MinimumLevel = minimum };
            var frozen = options with
            {
                CategoryLevels = new Dictionary<string, LogLevel>(options.CategoryLevels, StringComparer.Ordinal),
            };
            lock (Lifecycle)
            {
                if (_backend is not null) return;
                if (_shutdown is { IsCompleted: false })
                    throw new InvalidOperationException("Diagnostic logging is still shutting down.");
                try
                {
                    var directory = Path.Combine(Path.GetFullPath(dataDirectory), "logs");
                    Directory.CreateDirectory(directory);
                    var session = Guid.NewGuid().ToString("N")[..12];
                    Volatile.Write(ref _backend, new(directory, session, role, frozen));
                    _shutdown = null;
                    var logger = GetLogger("Diagnostics.Lifecycle");
                    logger.ZLogInformation(
                        $"Diagnostic logging initialized. Version={version}; Role={role}; ProcessId={Environment.ProcessId}; Session={session}; Architecture={RuntimeInformation.ProcessArchitecture.ToString()}; Runtime={RuntimeInformation.FrameworkDescription}; MinimumLevel={frozen.MinimumLevel.ToString()}; QueueCapacity={frozen.QueueCapacity}; ReservedErrorCapacity={frozen.ReservedErrorCapacity}");
                }
                catch (Exception error)
                {
                    ReportSinkFailure(error);
                }
            }
        }

        public static ValueTask ShutdownAsync()
        {
            lock (Lifecycle)
            {
                if (_shutdown is not null) return new(_shutdown);
                if (_backend is not { } backend) return ValueTask.CompletedTask;
                GetLogger("Diagnostics.Lifecycle").ZLogInformation(
                    $"Diagnostic logging stopping. DroppedEntries={DroppedEntries}; ErrorDrops={ErrorDrops}; SinkFailures={SinkFailures}");
                Volatile.Write(ref _lastFilePath, backend.CurrentFilePath);
                Volatile.Write(ref _backend, null);
                _shutdown = CompleteShutdownAsync(backend);
                return new(_shutdown);
            }
        }

        private static async Task CompleteShutdownAsync(DiagnosticLogBackend backend)
        {
            try
            {
                await backend.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                ReportSinkFailure(error);
            }
            finally
            {
                Volatile.Write(ref _lastFilePath, backend.CurrentFilePath);
            }
        }

        internal static void ReportDrop(LogLevel level)
        {
            Interlocked.Increment(ref _droppedEntries);
            if (level < LogLevel.Error) return;
            Interlocked.Increment(ref _errorDrops);
            if (Interlocked.Exchange(ref _reportedErrorDrop, 1) == 0)
                DiagnosticEmergency.Write(
                    "The diagnostic queue exhausted its reserved error capacity; inspect ErrorDrops");
        }

        internal static void ReportSinkFailure(Exception error)
        {
            Interlocked.Increment(ref _sinkFailures);
            if (Interlocked.Exchange(ref _reportedSinkFailure, 1) == 0)
                DiagnosticEmergency.Write("The diagnostic log sink failed", error);
        }
    }
}
