using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal static class PinnedHtmlView
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Pins.HtmlResources");

        internal static string PlainText(string html, PinnedHtmlSelection? selection = null)
        {
            return Parse(new(html, "") { Selection = selection }).PlainText;
        }

        internal static PinnedRichDocument Parse(PinnedHtmlDocument document)
        {
            if (document.Html.Length > PinnedContentRegistry.MaximumTextCharacters)
                throw new InvalidDataException(UiText.Get("PinnedTextTooLarge"));
            Uri.TryCreate(document.SourceUrl, UriKind.Absolute, out var baseUri);
            var parser = new PinnedRichParser();
            return document.Selection is { } selection
                ? parser.ParseFragment(document.Html, selection, baseUri)
                : parser.Parse(document.Html, baseUri);
        }

        internal static PinnedRichView Create(PinnedHtmlDocument document, PinnedRichResourceLoader resources)
        {
            return new(Parse(document), resources, AppTheme.Ink, false);
        }

        internal static PinnedRichResourceLoader CreateResources()
        {
            var store = new PinnedRichResourceStore(() =>
            {
                var directory = OwnedWorkerDirectory.Create("html-assets");
                return (directory.DirectoryPath, directory);
            }, 8L * 1024 * 1024, 64L * 1024 * 1024, Logger);
            return new(ApplicationHttp.CreateClient(), DecodeAsync, store, 64L * 1024 * 1024, 3, Logger);
        }

        private static Task<Bitmap> DecodeAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            return Task.Run(() =>
            {
                var content = PinnedImageContent.Decode(bytes, token);
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(content.SdrPng ?? content.Png, false);
                var bitmap = new Bitmap(stream);
                if (!token.IsCancellationRequested) return bitmap;
                bitmap.Dispose();
                token.ThrowIfCancellationRequested();
                throw new OperationCanceledException(token);
            }, token);
        }
    }
}
