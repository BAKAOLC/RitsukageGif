using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedWindowPlatform
    {
        private static bool _linuxInputAvailable;

        [SupportedOSPlatform("linux")]
        private static bool CanSetLinuxMouseThrough()
        {
            if (_linuxInputAvailable) return true;
            var executable = Path.Combine(AppContext.BaseDirectory, "RitsukageGif.X11Shortcuts");
            if (!File.Exists(executable)) return false;
            try
            {
                using var process = new Process();
                process.StartInfo = new()
                {
                    FileName = executable, UseShellExecute = false,
                    CreateNoWindow = true, RedirectStandardOutput = true,
                    ArgumentList = { "--xfixes-input-capability" },
                };
                if (!process.Start()) return false;
                if (!process.WaitForExit(1500))
                {
                    process.Kill();
                    _ = process.WaitForExit(1500);
                    return false;
                }

                return _linuxInputAvailable =
                    process.ExitCode == 0 && process.StandardOutput.ReadToEnd().Trim() == "OK";
            }
            catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
            {
                return false;
            }
        }
    }
}
