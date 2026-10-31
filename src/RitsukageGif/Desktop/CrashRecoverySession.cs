using System.ComponentModel;
using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class CrashRecoverySession
    {
        private static CrashRecoverySession? _current;
        private readonly Lock _gate = new();
        private bool _completed;
        private CrashRecoveryReport? _record;

        private CrashRecoverySession()
        {
        }

        internal static CrashRecoverySession? Current => Volatile.Read(ref _current);
        internal string? SessionPath { get; private set; }

        internal string? ReportPath
        {
            get
            {
                lock (_gate)
                    return _record is { } record && SessionPath is { } path
                        ? CrashRecoveryStorage.ReportPath(path, record.SessionId)
                        : null;
            }
        }

        internal static CrashRecoverySession Begin(string logDirectory)
        {
            var session = new CrashRecoverySession();
            try
            {
                Directory.CreateDirectory(logDirectory);
                using var process = Process.GetCurrentProcess();
                var now = DateTimeOffset.UtcNow;
                var record = new CrashRecoveryReport
                {
                    SessionId = Guid.NewGuid().ToString("N"), ProcessId = Environment.ProcessId,
                    ProcessStartedUtc = process.StartTime.ToUniversalTime(), StartedUtc = now,
                    LastOperationUtc = now, Version = ApplicationVersion.Display,
                };
                var path = Path.Combine(logDirectory, "rg-session-" + record.SessionId + ".json");
                if (CrashRecoveryStorage.Write(path, record))
                {
                    session._record = record;
                    session.SessionPath = path;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
            {
            }

            Volatile.Write(ref _current, session);
            return session;
        }

        internal static string? RecordStartupFailure(string logDirectory, Exception error)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                using var process = Process.GetCurrentProcess();
                var now = DateTimeOffset.UtcNow;
                var report = new CrashRecoveryReport
                {
                    SessionId = Guid.NewGuid().ToString("N"), ProcessId = Environment.ProcessId,
                    ProcessStartedUtc = process.StartTime.ToUniversalTime(), StartedUtc = now,
                    LastOperation = "Application startup", LastOperationUtc = now,
                    Version = ApplicationVersion.Display, Reason = "StartupFailure", Detail = error.ToString(),
                };
                var path = Path.Combine(logDirectory, "rg-failure-" + report.SessionId + ".json");
                return CrashRecoveryStorage.Write(path, report) ? path : null;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or Win32Exception)
            {
                return null;
            }
        }

        internal void MarkOperation(string operation)
        {
            lock (_gate)
            {
                if (_completed || _record is not { } record || SessionPath is not { } path ||
                    operation == record.LastOperation) return;
                _record = record with { LastOperation = operation, LastOperationUtc = DateTimeOffset.UtcNow };
                CrashRecoveryStorage.Write(path, _record);
            }
        }

        internal void RecordFailure(Exception error)
        {
            lock (_gate)
            {
                if (_completed || _record is not { } record || ReportPath is not { } path) return;
                _record = record with { Reason = "ManagedFailure", Detail = error.ToString() };
                var previous = CrashRecoveryStorage.Read(path);
                CrashRecoveryStorage.Write(path, _record with { Acknowledged = previous?.Acknowledged ?? false });
            }
        }

        internal void Complete(int exitCode)
        {
            lock (_gate)
            {
                if (_completed) return;
                _completed = true;
                if (_record is { } record && SessionPath is { } path &&
                    (exitCode == 0 || CrashRecoveryStorage.ReportExit(path, record, exitCode)))
                    CrashRecoveryStorage.Delete(path);
                Interlocked.CompareExchange(ref _current, null, this);
            }
        }

        internal static IReadOnlyList<CrashRecoveryReport> PendingReports(string logDirectory)
        {
            try
            {
                if (!Directory.Exists(logDirectory)) return [];
                PruneReports(logDirectory);
                foreach (var path in Directory.EnumerateFiles(logDirectory, "rg-session-*.json"))
                {
                    var record = CrashRecoveryStorage.Read(path);
                    if (record is null || CrashSessionMonitor.IsAlive(record) != false) continue;
                    if (!CrashRecoveryStorage.ReportExit(path, record, null)) continue;
                    CrashRecoveryStorage.Delete(path);
                    CrashRecoveryStorage.Delete(path + ".tmp");
                }

                return
                [
                    .. Directory.EnumerateFiles(logDirectory, "rg-failure-*.json")
                        .Select(CrashRecoveryStorage.Read).OfType<CrashRecoveryReport>()
                        .Where(report => !report.Acknowledged)
                        .OrderByDescending(report => report.StartedUtc),
                ];
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        internal static void Acknowledge(string reportPath)
        {
            var report = CrashRecoveryStorage.Read(reportPath);
            if (report is not null) CrashRecoveryStorage.Write(reportPath, report with { Acknowledged = true });
        }

        private static void PruneReports(string logDirectory)
        {
            var policy = new DiagnosticsOptions();
            FileInfo[] files =
            [
                .. new DirectoryInfo(logDirectory).GetFiles("rg-failure-*.json")
                    .Where(file => CrashRecoveryStorage.Read(file.FullName)?.Acknowledged == true)
                    .OrderByDescending(file => file.LastWriteTimeUtc),
            ];
            var cutoff = DateTime.UtcNow - policy.MaximumAge;
            for (var index = 0; index < files.Length; index++)
                if (index >= policy.MaximumFiles || files[index].LastWriteTimeUtc < cutoff)
                    CrashRecoveryStorage.Delete(files[index].FullName);
        }
    }
}
