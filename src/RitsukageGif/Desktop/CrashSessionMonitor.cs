using System.ComponentModel;
using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal static class CrashSessionMonitor
    {
        internal static int Run(int processId, string sessionPath)
        {
            var session = CrashRecoveryStorage.Read(sessionPath);
            if (session is null || session.ProcessId != processId ||
                Path.GetFileName(sessionPath) != "rg-session-" + session.SessionId + ".json") return 0;
            int? exitCode = null;
            try
            {
                using var parent = Process.GetProcessById(processId);
                if (OperatingSystem.IsWindows()) _ = parent.SafeHandle;
                if (parent.StartTime.ToUniversalTime() != session.ProcessStartedUtc.UtcDateTime) return 0;
                parent.WaitForExit();
                if (OperatingSystem.IsWindows()) exitCode = parent.ExitCode;
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
                return 0;
            }

            session = CrashRecoveryStorage.Read(sessionPath);
            if (session is null) return 0;
            if (CrashRecoveryStorage.ReportExit(sessionPath, session, exitCode))
            {
                CrashRecoveryStorage.Delete(sessionPath);
                CrashRecoveryStorage.Delete(sessionPath + ".tmp");
            }

            return 0;
        }

        internal static bool? IsAlive(CrashRecoveryReport session)
        {
            try
            {
                using var process = Process.GetProcessById(session.ProcessId);
                return !process.HasExited &&
                       process.StartTime.ToUniversalTime() == session.ProcessStartedUtc.UtcDateTime;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return null;
            }
        }
    }
}
