using System.ComponentModel;
using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal static class PlatformShell
    {
        private static Process? _sound;
        private static readonly object SoundLock = new();

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

        public static void PlaySound(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                NativeMethods.PlaySound(path, 0, 0x20003);
                return;
            }

            lock (SoundLock)
            {
                StopSoundCore();
                try
                {
                    var start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/afplay" : "aplay")
                        { UseShellExecute = false, CreateNoWindow = true };
                    if (OperatingSystem.IsLinux()) start.ArgumentList.Add("-q");
                    start.ArgumentList.Add(Path.GetFullPath(path));
                    _sound = Process.Start(start);
                }
                catch (Win32Exception)
                {
                }
            }
        }

        public static void StopSound()
        {
            if (OperatingSystem.IsWindows()) NativeMethods.PlaySound(null, 0, 0);
            else
                lock (SoundLock)
                    StopSoundCore();
        }

        private static void StopSoundCore()
        {
            if (_sound is null) return;
            try
            {
                if (!_sound.HasExited) _sound.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                _sound.Dispose();
                _sound = null;
            }
        }
    }
}
