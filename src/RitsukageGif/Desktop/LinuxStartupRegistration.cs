namespace RitsukageGif.Desktop
{
    internal sealed class LinuxStartupRegistration(string configHome, IReadOnlyList<string> systemConfigDirectories)
        : FileStartupRegistration(Path.Combine(configHome, "autostart", EntryName))
    {
        private const string EntryName = "ritsukagegif.desktop";
        private const string DisabledEntry = "[Desktop Entry]\nType=Application\nName=RitsukageGif\nHidden=true\n";

        public LinuxStartupRegistration() : this(ConfigHome(), ConfigDirectories())
        {
        }

        public override string? Read()
        {
            if (base.Read() is { } entry) return entry;
            var inherited = InheritedEntry();
            return inherited is null ? null : File.ReadAllText(inherited);
        }

        public override void Write(string? command)
        {
            base.Write(command ?? (InheritedEntry() is null ? null : DisabledEntry));
        }

        public override string CreateCommand(string executable)
        {
            var path = Path.GetFullPath(executable);
            return "[Desktop Entry]\nType=Application\nName=RitsukageGif\nExec=" +
                   DesktopEntryEncoding.ExecCommand(path) +
                   " --serve\nTerminal=false\nHidden=false\n";
        }

        public override bool IsEnabled(string executable)
        {
            if (Read() is not { } entry) return false;
            var fields = DesktopEntryEncoding.Fields(entry);
            return !string.Equals(fields.GetValueOrDefault("Hidden"), "true", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(fields.GetValueOrDefault("Type"), "Application", StringComparison.Ordinal) &&
                   string.Equals(fields.GetValueOrDefault("Exec"), DesktopEntryEncoding.ExecCommand(executable) +
                                                                   " --serve", StringComparison.Ordinal);
        }

        private string? InheritedEntry()
        {
            return systemConfigDirectories.Select(directory => Path.Combine(directory, "autostart", EntryName))
                .FirstOrDefault(File.Exists);
        }

        private static string ConfigHome()
        {
            var configured = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return configured is not null && Path.IsPathFullyQualified(configured)
                ? configured
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        private static string[] ConfigDirectories()
        {
            var configured = Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS");
            return
            [
                .. (string.IsNullOrEmpty(configured) ? "/etc/xdg" : configured).Split(':')
                .Where(Path.IsPathFullyQualified),
            ];
        }
    }
}
