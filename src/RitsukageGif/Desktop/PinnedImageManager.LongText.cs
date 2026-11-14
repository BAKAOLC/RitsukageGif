using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private const int LongClipboardTextThreshold = 2048;

        private PinnedInputResult CommitOrDeferText(PinnedClipboardSnapshot snapshot, PinnedDocument document,
            bool usePlainText)
        {
            if (Preferences.ConfirmLongTextPaste &&
                snapshot.Text is { Length: > LongClipboardTextThreshold } text &&
                document is PinnedTextDocument or PinnedHtmlDocument)
            {
                if (HandleDuplicateDocument(document)) return new(true, null);

                return new(true, null)
                {
                    LongText = (document, DocumentSourceMime(snapshot, usePlainText),
                        text.Length),
                };
            }

            return PinSourceDocument(snapshot, document, usePlainText);
        }

        private async Task<PinnedInputResult> ConfirmClipboardPinAsync(PinnedInputResult result, Window? owner,
            CancellationToken token)
        {
            if (result.LongText is not { } text) return result;

            var dialog = new PinnedLongTextDialog(text.Length);
            var accepted = await dialog.AskAsync(owner, token);
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (dialog.DoNotAskAgain && Preferences.ConfirmLongTextPaste)
            {
                Preferences = Preferences with { ConfirmLongTextPaste = false };
                PreferencesChanged?.Invoke(Preferences);
            }

            if (!accepted) return new(true, null);
            try
            {
                return CompleteSourceInput(text.Source, PinDocument(text.Document));
            }
            catch (PinnedHtmlFormatException error) when (text.Document is PinnedHtmlDocument html)
            {
                LogClipboardHtmlFallback(error, true);
                return CompleteSourceInput(text.Source, PinDocument(new PinnedTextDocument(html.Text)));
            }
        }
    }
}
