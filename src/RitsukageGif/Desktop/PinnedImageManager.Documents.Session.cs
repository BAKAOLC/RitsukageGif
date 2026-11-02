using System.Text.Json;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly Dictionary<PinnedDocumentWindow, PinnedDocumentState> _pendingDocuments = [];
        private readonly Dictionary<string, PinnedDocumentState> _unrestoredDocuments = new(StringComparer.Ordinal);

        private async Task<int> ReadDocumentsAsync(bool restore, CancellationToken token, string? group = null)
        {
            var path = Path.Combine(_sessionDirectory!, "documents.json");
            if (!File.Exists(path)) return 0;
            if (new FileInfo(path).Length > MaximumSessionBytes) return 1;
            await using var stream = File.OpenRead(path);
            using var saved = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            if (saved.RootElement.ValueKind != JsonValueKind.Array) return 1;

            var rejected = 0;
            foreach (var element in saved.RootElement.EnumerateArray())
            {
                PinnedDocumentState? entry;
                try
                {
                    entry = element.Deserialize(PinnedImageJsonContext.Default.PinnedDocumentState);
                }
                catch (JsonException)
                {
                    RejectSessionRecord("documents.json", element);
                    rejected++;
                    continue;
                }

                if (group is not null && entry is { Window.Group: var entryGroup } && entryGroup != group) continue;
                token.ThrowIfCancellationRequested();
                if (entry is null || !ValidState(entry.Window) || !ValidDocument(entry.Document) ||
                    !double.IsFinite(entry.Width) || entry.Width is < 120 or > 32768 ||
                    !double.IsFinite(entry.Height) || entry.Height is < 72 or > 32768)
                {
                    RejectSessionRecord("documents.json", element);
                    rejected++;
                    continue;
                }

                if (_documentWindows.Any(window => window.Id == entry.Window.Id))
                {
                    _unrestoredDocuments.Remove(entry.Window.Id);
                    continue;
                }

                try
                {
                    if (entry.Document is PinnedHtmlDocument html) _ = PinnedRichParser.ReadHtml(html.Html);
                }
                catch (PinnedHtmlFormatException error)
                {
                    RejectSessionRecord("documents.json", element);
                    PinLogger.ZLogWarning(
                        $"Stored HTML formatting was rejected before restoration. MaximumNestingDepth={PinnedRichParser.MaximumNestingDepth}; ErrorType={error.GetType().Name} Code={error.HResult}");
                    rejected++;
                    continue;
                }

                _unrestoredDocuments[entry.Window.Id] = entry;
                if (!restore) continue;
                PinnedDocumentWindow? window = null;
                try
                {
                    window = CreateDocumentWindow(entry.Document);
                    window.RestoreDocumentState(entry);
                    if (entry.Window.Group != Preferences.ActiveGroup)
                    {
                        var visible = entry.Window.GroupHidden ? entry.Window.VisibleInGroup : entry.Window.Visible;
                        window.Presentation.HideForGroup(visible);
                        entry.Window.GroupHidden = true;
                        entry.Window.VisibleInGroup = visible;
                        entry.Window.Visible = false;
                    }

                    if (entry.Window.Group != Preferences.ActiveGroup ||
                        (!entry.Window.Visible && CanRestoreHidden()) || !CanRestoreNow())
                    {
                        _pendingDocuments.Add(window, entry);
                    }
                    else
                    {
                        window.ShowActivated = false;
                        window.Show();
                        window.RestoreDocumentState(entry);
                    }

                    _unrestoredDocuments.Remove(entry.Window.Id);
                }
                catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
                {
                    if (window is not null)
                    {
                        _documentWindows.Remove(window);
                        _pendingDocuments.Remove(window);
                        window.Dispose();
                    }

                    PinLogger.ZLogWarning(
                        $"Pinned document restoration failed. ErrorType={error.GetType().Name} Code={error.HResult}");
                    rejected++;
                }
            }

            return rejected;
        }

        private static bool ValidDocument(PinnedDocument? document)
        {
            return document switch
            {
                PinnedTextDocument text => text.Text is { Length: <= PinnedContentRegistry.MaximumTextCharacters },
                PinnedHtmlDocument
                    {
                        Html.Length: <= PinnedContentRegistry.MaximumTextCharacters,
                        Text.Length: <= PinnedContentRegistry.MaximumTextCharacters,
                    } html =>
                    html.Selection is null || html.Selection.IsValidFor(html.Html),
                PinnedColorDocument
                    {
                        Text.Length: <= PinnedContentRegistry.MaximumTextCharacters,
                        Html.Length: <= PinnedContentRegistry.MaximumTextCharacters,
                    } color =>
                    Color.TryParse(color.Value, out _),
                PinnedFormulaDocument formula => formula.Sources is { Length: > 0 } &&
                                                 formula.Sources.All(source => source is
                                                 {
                                                     Length: > 0 and <= PinnedContentRegistry.MaximumTextCharacters,
                                                 }) && formula.Sources.Sum(source => (long)source.Length) <=
                                                 PinnedContentRegistry.MaximumTextCharacters &&
                                                 float.IsFinite(formula.FontSize) &&
                                                 formula.FontSize is >= 1 and <= 512,
                PinnedFilesDocument files => files.Paths is { Length: > 0 } &&
                                             files.Paths.All(static path =>
                                                 // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                                                 path is not null && Path.IsPathFullyQualified(path)),
                _ => false,
            };
        }

        private async Task WriteDocumentsAsync(CancellationToken token)
        {
            var saved = _documentWindows.Select(window => _pendingDocuments.TryGetValue(window, out var pending)
                    ? pending
                    : window.CaptureDocumentState())
                .Concat(_unrestoredDocuments.Values).ToArray();
            Directory.CreateDirectory(_sessionDirectory!);
            var path = Path.Combine(_sessionDirectory!, "documents.json");
            var staging = path + ".tmp";
            try
            {
                await using (var stream = File.Create(staging))
                    await JsonSerializer.SerializeAsync(stream, saved,
                        PinnedImageJsonContext.Default.PinnedDocumentStateArray, token);
                token.ThrowIfCancellationRequested();
                if (new FileInfo(staging).Length > MaximumSessionBytes)
                    throw new InvalidDataException(UiText.Get("PinnedImageSessionTooLarge"));
                File.Move(staging, path, true);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }

        private void ResumeDocumentWindows()
        {
            foreach (var (window, saved) in _pendingDocuments.ToArray())
            {
                if (saved.Window.Group != Preferences.ActiveGroup ||
                    (!saved.Window.Visible && CanRestoreHidden())) continue;
                _pendingDocuments.Remove(window);
                window.Show();
                window.RestoreDocumentState(saved);
            }
        }

        private void RevealWindow(IPinnedWindow window)
        {
            switch (window)
            {
                case PinnedImageWindow image:
                    RevealWindow(image);
                    return;
                case PinnedDocumentWindow document when _pendingDocuments.Remove(document, out var saved):
                    saved.Window.Visible = true;
                    document.Show();
                    document.RestoreDocumentState(saved);
                    break;
            }

            window.RevealImage();
        }
    }
}
