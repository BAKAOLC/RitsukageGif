namespace RitsukageGif.Desktop
{
    internal interface IStartupRegistration
    {
        string? Read();
        void Write(string? command);

        string CreateCommand(string executable)
        {
            return StartupRegistrationService.Command(executable);
        }

        bool IsEnabled(string executable)
        {
            return string.Equals(Read(), CreateCommand(executable), StringComparison.Ordinal);
        }

        StartupRegistrationStatus GetStatus(string executable)
        {
            return IsEnabled(executable)
                ? StartupRegistrationStatus.Enabled
                : StartupRegistrationStatus.Disabled;
        }
    }
}
