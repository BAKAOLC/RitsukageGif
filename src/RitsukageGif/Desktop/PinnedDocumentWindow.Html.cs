using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private void OpenHtmlLink(string target)
        {
            if (Document is not PinnedHtmlDocument html) return;
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
                if (!Uri.TryCreate(html.SourceUrl, UriKind.Absolute, out var source) ||
                    !Uri.TryCreate(source, target, out uri))
                    return;
            if (uri.Scheme is not ("https" or "http" or "mailto")) return;
            _ = RunAsync(() =>
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return Task.CompletedTask;
            });
        }
    }
}
