using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static class CrashRecoveryStorage
    {
        internal static CrashRecoveryReport? Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (input.Length > 1024 * 1024) return null;
                var report = JsonSerializer.Deserialize(input, CrashRecoveryJsonContext.Default.CrashRecoveryReport);
                return report is not null && Guid.TryParseExact(report.SessionId, "N", out _) && report.ProcessId > 0
                    ? report with { Path = path }
                    : null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }

        internal static bool Write(string path, CrashRecoveryReport report)
        {
            var temporary = path + ".tmp";
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(report,
                    CrashRecoveryJsonContext.Default.CrashRecoveryReport);
                using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                           4096, FileOptions.WriteThrough))
                {
                    output.Write(bytes);
                    output.Flush(true);
                }

                File.Move(temporary, path, true);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                Delete(temporary);
            }
        }

        internal static void Delete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        internal static string ReportPath(string sessionPath, string sessionId)
        {
            return Path.Combine(Path.GetDirectoryName(sessionPath)!,
                "rg-failure-" + sessionId + ".json");
        }

        internal static bool ReportExit(string sessionPath, CrashRecoveryReport session, int? exitCode)
        {
            var reportPath = ReportPath(sessionPath, session.SessionId);
            var previous = Read(reportPath);
            var report = (previous ?? session) with
            {
                Path = reportPath,
                Reason = previous?.Reason is "ManagedFailure" ? "ManagedFailure" : "UnexpectedExit",
                ExitCode = exitCode ?? previous?.ExitCode,
            };
            return Write(reportPath, report);
        }
    }
}
