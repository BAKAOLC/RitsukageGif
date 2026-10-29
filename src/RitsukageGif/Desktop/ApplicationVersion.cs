using System.Reflection;

namespace RitsukageGif.Desktop
{
    internal static class ApplicationVersion
    {
        // Release comparisons use the numeric baseline, so a development build never offers its old base release.
        public static Version Numeric { get; } =
            typeof(ApplicationVersion).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

        public static string Display { get; } = typeof(ApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Numeric.ToString();
    }
}
