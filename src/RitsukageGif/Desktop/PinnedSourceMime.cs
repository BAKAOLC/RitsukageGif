using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedSourceMime(string? Text, string? Html)
    {
        internal bool HasText => !string.IsNullOrEmpty(Text);
        internal bool HasHtml => !string.IsNullOrEmpty(Html);
        internal long EstimatedBytes => ((long)(Text?.Length ?? 0) + (Html?.Length ?? 0)) * sizeof(char);

        internal static PinnedSourceMime? Create(string? text, string? html)
        {
            if (string.IsNullOrEmpty(text)) text = null;
            if (string.IsNullOrEmpty(html)) html = null;
            if (text is null && html is null) return null;
            return new(text, html);
        }

        internal static PinnedSourceMime? FromDocument(PinnedDocument document)
        {
            return document switch
            {
                PinnedTextDocument text => Create(text.Text, null),
                PinnedHtmlDocument html => Create(html.Text, html.Html),
                PinnedFilesDocument files => Create(string.Join('\n', files.Paths.Select(Path.GetFileName)), null),
                _ => null,
            };
        }

        internal DataTransfer CreateTransfer(PinnedSourceMimeFormat format)
        {
            var item = new DataTransferItem();
            if (format == PinnedSourceMimeFormat.PlainText)
            {
                item.Set(DataFormat.Text, Text ?? "");
            }
            else
            {
                if (format == PinnedSourceMimeFormat.Html) item.Set(DataFormat.Text, Text ?? "");
                ApplyTo(item);
            }

            var transfer = new DataTransfer();
            transfer.Add(item);
            return transfer;
        }

        internal void ApplyTo(DataTransferItem item)
        {
            if (HasText) item.Set(DataFormat.Text, Text);
            if (!HasHtml) return;
            ClipboardPlatformFormats.SetHtml(item, Html!);
            if (OperatingSystem.IsWindows())
                item.Set(DataFormat.CreateBytesPlatformFormat("HTML Format"), CreateWindowsHtml(Html!));
        }

        private static byte[] CreateWindowsHtml(string html)
        {
            const string start = "<!--StartFragment-->";
            const string end = "<!--EndFragment-->";
            if (!html.Contains(start, StringComparison.Ordinal)) html = start + html;
            if (!html.Contains(end, StringComparison.Ordinal)) html += end;
            return ClipboardHtml.CreateWindowsContext(html, PinnedHtmlSelection.FromMarkers(html)!);
        }
    }
}
