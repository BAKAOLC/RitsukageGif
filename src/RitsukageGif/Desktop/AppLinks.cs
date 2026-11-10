using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal static class AppLinks
    {
        internal const string DocumentationUrl = "https://github.com/BAKAOLC/RitsukageGif/blob/HEAD/docs/README.md";

        internal static void OpenExternal(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
    }
}
