using System.Diagnostics;
using System.Security.Principal;

namespace RitsukageGif.Desktop
{
    internal static class StartupRegistrationService
    {
        internal static string Command(string executable)
        {
            var path = Path.GetFullPath(executable);
            if (path.Contains('"'))
                throw new ArgumentException("The executable path contains a quote.", nameof(executable));
            return "\"" + path + "\" --startup";
        }

        internal static StartupRegistrationStatus Save(bool enabled, string executable, Action persist,
            IStartupRegistration registration)
        {
            var previous = registration.Read();
            var command = enabled ? registration.CreateCommand(executable) : null;
            if (string.Equals(previous, command, StringComparison.Ordinal))
            {
                persist();
                return registration.GetStatus(executable);
            }

            try
            {
                registration.Write(command);
                var status = registration.GetStatus(executable);
                if (status != StartupRegistrationStatus.Disabled != enabled)
                    throw new InvalidOperationException("The login startup entry did not match the requested state.");
                persist();
                return status;
            }
            catch (Exception original)
            {
                try
                {
                    registration.Write(previous);
                }
                catch (Exception rollback)
                {
                    throw new AggregateException(original, rollback);
                }

                throw;
            }
        }

        internal static bool RelaunchIfRequired(SystemPreferences preferences, string[] arguments)
        {
            if (!OperatingSystem.IsWindows() || !preferences.RunAsAdministrator ||
                arguments is not ([] or ["--serve"] or ["--silent"] or ["--startup"])) return false;
            if (IsRunningAsAdministrator()) return false;
            if (Environment.ProcessPath is not { } executable) return false;
            var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            return process is not null;
        }

        internal static bool IsRunningAsAdministrator()
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
