using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal static class PlatformShell
    {
        private static readonly SoundPlayback Sound = new();

        public static void OpenDirectory(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var directory = Path.GetFullPath(path);
            Directory.CreateDirectory(directory);
            var start = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "explorer.exe" :
                    OperatingSystem.IsMacOS() ? "/usr/bin/open" : "xdg-open",
                UseShellExecute = false,
            };
            start.ArgumentList.Add(directory);
            Process.Start(start)?.Dispose();
        }

        public static void RevealFile(string path)
        {
            var start = new ProcessStartInfo { UseShellExecute = false };
            if (OperatingSystem.IsWindows())
            {
                start.FileName = "explorer.exe";
                start.ArgumentList.Add("/select," + Path.GetFullPath(path));
            }
            else if (OperatingSystem.IsMacOS())
            {
                start.FileName = "/usr/bin/open";
                start.ArgumentList.Add("-R");
                start.ArgumentList.Add(Path.GetFullPath(path));
            }
            else
            {
                start.FileName = "xdg-open";
                start.ArgumentList.Add(Path.GetDirectoryName(Path.GetFullPath(path))!);
            }

            Process.Start(start)?.Dispose();
        }

        public static Task PlaySoundAsync(string path)
        {
            return Sound.PlayAsync(path);
        }

        public static async Task StopSoundAsync()
        {
            try
            {
                await Sound.StopAsync();
            }
            catch (Exception ex)
            {
                Trace.TraceError("Audio playback failed: {0}", ex);
            }
        }
    }
}
