using System.Text;

namespace RitsukageGif.Desktop
{
    internal sealed class MacServiceStartupRegistration(
        string executable,
        IMacLoginService service,
        MacLaunchAgentRegistration standalone) : IStartupRegistration
    {
        internal const string PlistName = "local.ritsukagegif.login.plist";

        public string CreateCommand(string currentExecutable)
        {
            EnsureExecutable(currentExecutable);
            return "1:";
        }

        public string? Read()
        {
            var enabled = service.Status != StartupRegistrationStatus.Disabled;
            var previous = standalone.Read();
            return !enabled && previous is null
                ? null
                : (enabled ? "1:" : "0:") +
                  (previous is null ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(previous)));
        }

        public void Write(string? command)
        {
            var enabled = command?.StartsWith("1:", StringComparison.Ordinal) == true;
            string? previous = null;
            if (command is { Length: > 2 }) previous = Encoding.UTF8.GetString(Convert.FromBase64String(command[2..]));
            if (service.Status != StartupRegistrationStatus.Disabled != enabled) service.SetEnabled(enabled);
            standalone.Write(previous);
        }

        public bool IsEnabled(string currentExecutable)
        {
            return GetStatus(currentExecutable) == StartupRegistrationStatus.Enabled;
        }

        public StartupRegistrationStatus GetStatus(string currentExecutable)
        {
            EnsureExecutable(currentExecutable);
            var status = service.Status;
            return status == StartupRegistrationStatus.Disabled && standalone.IsEnabled(currentExecutable)
                ? StartupRegistrationStatus.Enabled
                : status;
        }

        internal static IStartupRegistration? TryCreate()
        {
            if (!OperatingSystem.IsMacOSVersionAtLeast(13) || Environment.ProcessPath is not { } executable)
                return null;
            var macos = Path.GetDirectoryName(executable);
            var contents = Path.GetDirectoryName(macos);
            if (Path.GetFileName(macos) != "MacOS" || Path.GetFileName(contents) != "Contents" ||
                !File.Exists(Path.Combine(contents!, "Library", "LaunchAgents", PlistName))) return null;
            return new MacServiceStartupRegistration(executable, new MacLoginService(),
                new());
        }

        private void EnsureExecutable(string currentExecutable)
        {
            if (!string.Equals(Path.GetFullPath(currentExecutable), Path.GetFullPath(executable),
                    StringComparison.Ordinal))
                throw new ArgumentException("The login item does not belong to this executable.",
                    nameof(currentExecutable));
        }
    }
}
