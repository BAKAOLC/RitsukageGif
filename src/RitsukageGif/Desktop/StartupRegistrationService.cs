using System.Diagnostics;
using System.Security.Principal;

namespace RitsukageGif.Desktop
{
    internal static class StartupRegistrationService
    {
        private static string Command(string executable)
        {
            var path = Path.GetFullPath(executable);
            if (path.Contains('"'))
                throw new ArgumentException("The executable path contains a quote.", nameof(executable));
            return "\"" + path + "\" --serve";
        }

        internal static void Save(bool enabled, string executable, Action persist,
            IStartupRegistration registration)
        {
            var previous = registration.Read();
            var command = enabled ? Command(executable) : null;
            if (string.Equals(previous, command, StringComparison.Ordinal))
            {
                persist();
                return;
            }

            try
            {
                registration.Write(command);
                persist();
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
                arguments is not ([] or ["--serve"])) return false;
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return false;
            if (Environment.ProcessPath is not { } executable) return false;
            var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            return process is not null;
        }
    }
}
