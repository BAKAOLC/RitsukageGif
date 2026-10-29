using System.ComponentModel;
using System.Diagnostics;

namespace RitsukageGif.Diagnostics
{
    internal static class DiagnosticRetention
    {
        internal static void Apply(string directory, DiagnosticsOptions options)
        {
            try
            {
                var files = new DirectoryInfo(directory).GetFiles("rg-*.log")
                    .OrderBy(file => file.LastWriteTimeUtc).ToArray();
                var count = files.Length;
                var bytes = files.Sum(file => file.Length);
                var cutoff = DateTime.UtcNow - options.MaximumAge;
                foreach (var file in files)
                {
                    if (file.LastWriteTimeUtc >= cutoff && count <= options.MaximumFiles &&
                        bytes <= options.MaximumTotalBytes) continue;
                    if (IsOtherLiveProcess(file.Name)) continue;
                    try
                    {
                        using var guard = new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite,
                            FileShare.Delete);
                        File.Delete(file.FullName);
                        count--;
                        bytes -= file.Length;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                LogHub.ReportSinkFailure(error);
            }
        }

        private static bool IsOtherLiveProcess(string name)
        {
            var end = name.IndexOf('-', 3);
            if (end < 0 || !int.TryParse(name.AsSpan(3, end - 3), out var processId) ||
                processId == Environment.ProcessId) return false;
            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
