namespace RitsukageGif.Desktop
{
    internal static class DesktopApplicationRegistration
    {
        private const string EntryName = "ritsukagegif.desktop";
        private const string OwnershipKey = "X-RitsukageGif-Owned";

        internal static void EnsureRegistered()
        {
            if (!OperatingSystem.IsLinux()) return;
            var home = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(home) || !Path.IsPathFullyQualified(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local",
                    "share");
            var directories = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            EnsureRegistered(
                Environment.ProcessPath ??
                throw new InvalidOperationException("The application executable is unavailable."),
                home, [
                    .. (string.IsNullOrEmpty(directories) ? "/usr/local/share:/usr/share" : directories).Split(':')
                    .Where(Path.IsPathFullyQualified),
                ]);
        }

        private static void EnsureRegistered(string executable, string dataHome,
            IReadOnlyList<string> systemDirectories)
        {
            var directory = Path.Combine(dataHome, "applications");
            var entryPath = Path.Combine(directory, EntryName);
            if (File.Exists(entryPath))
            {
                var existing = File.ReadAllText(entryPath);
                if (DesktopEntryEncoding.MatchesExecutable(existing, executable)) return;
                if (DesktopEntryEncoding.Fields(existing).GetValueOrDefault(OwnershipKey) != "true")
                    throw new IOException(
                        "A different user application owns ritsukagegif.desktop; it was not overwritten.");
            }
            else if (systemDirectories.Select(path => Path.Combine(path, "applications", EntryName))
                     .Where(File.Exists).Any(path =>
                         DesktopEntryEncoding.MatchesExecutable(File.ReadAllText(path), executable)))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            var entry = "[Desktop Entry]\nType=Application\nName=RitsukageGif\nExec=" +
                        DesktopEntryEncoding.ExecCommand(executable) +
                        "\nTerminal=false\nCategories=Graphics;Recorder;\nStartupWMClass=RitsukageGif\n" +
                        OwnershipKey + "=true\n";
            var temporary = entryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, entry);
                File.Move(temporary, entryPath, true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }
}
