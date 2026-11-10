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
            var command = Read();
            return string.Equals(command, CreateCommand(executable), StringComparison.Ordinal) ||
                   string.Equals(command, "\"" + Path.GetFullPath(executable) + "\" --serve",
                       StringComparison.Ordinal);
        }

        StartupRegistrationStatus GetStatus(string executable)
        {
            return IsEnabled(executable)
                ? StartupRegistrationStatus.Enabled
                : StartupRegistrationStatus.Disabled;
        }
    }
}
