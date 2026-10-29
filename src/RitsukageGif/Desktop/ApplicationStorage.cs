namespace RitsukageGif.Desktop
{
    internal static class ApplicationStorage
    {
        private static readonly string?
            CustomDirectory = Environment.GetEnvironmentVariable("RITSUKAGE_DATA_DIRECTORY");

        private static readonly Lazy<string> RootDirectory = new(ResolveDirectory);

        internal static bool IsCustomDirectory => !string.IsNullOrWhiteSpace(CustomDirectory);

        internal static string DataDirectory => RootDirectory.Value;

        private static string ResolveDirectory()
        {
            if (!IsCustomDirectory)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RitsukageGif");
            if (!Path.IsPathFullyQualified(CustomDirectory!))
                throw new ArgumentException(UiText.Get("SettingsDirectoryAbsolute"));
            return Path.GetFullPath(CustomDirectory!);
        }
    }
}
