using System.Diagnostics.CodeAnalysis;

namespace RitsukageGif.Desktop
{
    internal static class PinnedContentRegistry
    {
        public const int MaximumTextCharacters = 2 * 1024 * 1024;

        private static readonly (PinnedContentKinds Kind, Func<PinnedClipboardSnapshot, PinnedDocument?> Create)[]
            Handlers =
            [
                (PinnedContentKinds.Files,
                    snapshot => snapshot.Files.Length > 0 ? new PinnedFilesDocument(snapshot.Files) : null),
                (PinnedContentKinds.Color, snapshot => TryColor(snapshot.Text, out var color)
                    ? new PinnedColorDocument(color, snapshot.Text!,
                        snapshot is { Html: { } html, HtmlSelection: { } selection }
                            ? selection.Extract(html)
                            : snapshot.Html ?? "")
                    : null),
                (PinnedContentKinds.Formula,
                    snapshot => string.IsNullOrWhiteSpace(snapshot.FormulaSource)
                        ? PinnedFormulaInput.TryCreate(snapshot.Text)
                        : new([snapshot.FormulaSource])),
                (PinnedContentKinds.Text, snapshot => !HasHtml(snapshot, out var html)
                    ? null
                    : new PinnedHtmlDocument(html,
                            snapshot.Text ?? PinnedHtmlView.PlainText(html, snapshot.HtmlSelection))
                        { SourceUrl = snapshot.HtmlSourceUrl, Selection = snapshot.HtmlSelection }),
                (PinnedContentKinds.Text,
                    snapshot => string.IsNullOrEmpty(snapshot.Text) ? null : new PinnedTextDocument(snapshot.Text)),
            ];

        private static bool HasHtml(PinnedClipboardSnapshot snapshot, [NotNullWhen(true)] out string? html)
        {
            html = snapshot.Html;
            if (html is null) return false;
            var selection = snapshot.HtmlSelection ?? PinnedHtmlSelection.FromMarkers(html);
            return !(selection is null ? html.AsSpan() : html.AsSpan(selection.Start, selection.Length)).IsWhiteSpace();
        }

        public static PinnedDocument? Create(PinnedClipboardSnapshot snapshot,
            PinnedContentKinds kinds = PinnedContentKinds.All)
        {
            foreach (var handler in Handlers)
            {
                if ((kinds & handler.Kind) == 0) continue;
                if (handler.Kind != PinnedContentKinds.Files &&
                    (snapshot.Text?.Length > MaximumTextCharacters || snapshot.Html?.Length > MaximumTextCharacters ||
                     snapshot.FormulaSource?.Length > MaximumTextCharacters))
                    throw new InvalidDataException(UiText.Get("PinnedTextTooLarge"));
                if (handler.Create(snapshot) is { } document) return document;
            }

            return null;
        }

        private static bool TryColor(string? text, out string color)
        {
            color = "";
            if (text is null) return false;
            if (!PinnedColorParser.TryParse(text, out var parsed)) return false;
            color = parsed.ToString();
            return true;
        }
    }
}
