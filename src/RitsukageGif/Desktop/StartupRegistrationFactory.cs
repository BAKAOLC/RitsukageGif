namespace RitsukageGif.Desktop
{
    internal static class StartupRegistrationFactory
    {
        internal static bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
                                          OperatingSystem.IsLinux();

        internal static IStartupRegistration Create()
        {
            if (OperatingSystem.IsWindows()) return new WindowsStartupRegistration();
            if (OperatingSystem.IsMacOS())
                return MacServiceStartupRegistration.TryCreate() ?? new MacLaunchAgentRegistration();
            if (OperatingSystem.IsLinux()) return new LinuxStartupRegistration();
            throw new PlatformNotSupportedException("Login startup is not supported on this platform.");
        }
    }
}
