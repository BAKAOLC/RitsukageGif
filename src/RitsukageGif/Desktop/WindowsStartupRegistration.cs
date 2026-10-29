using Microsoft.Win32;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowsStartupRegistration : IStartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "RitsukageGif";

        public string? Read()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }

        public void Write(string? command)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (command is null) key.DeleteValue(ValueName, false);
            else key.SetValue(ValueName, command, RegistryValueKind.String);
        }
    }
}
