using System.Text.Json;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly Dictionary<PinnedDocumentWindow, PinnedDocumentState> _pendingDocuments = [];
        private readonly Dictionary<string, PinnedDocumentState> _unrestoredDocuments = new(StringComparer.Ordinal);

        private async Task<int> ReadDocumentsAsync(bool restore, CancellationToken token, string? group = null,
            bool loadClosed = false)
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

                if (entry is { ClosedAtUtcTicks: > 0 } && !loadClosed) continue;
                if (group is not null && entry is { Window.Group: var entryGroup } && entryGroup != group) continue;
                token.ThrowIfCancellationRequested();
                if (entry is null || entry.ClosedAtUtcTicks < 0 || entry.ClosedAtUtcTicks > DateTime.MaxValue.Ticks ||
                    !ValidState(entry.Window, entry.Thumbnail is { Enabled: true }) ||
                    !ValidDocument(entry.Document) ||
                    (entry.Translation is { } translation &&
                     (entry.Document is not (PinnedTextDocument or PinnedHtmlDocument) || !translation.IsValid)) ||
                    !double.IsFinite(entry.Width) || entry.Width > 32768 ||
                    entry.Width < (entry.Thumbnail is { Enabled: true }
                        ? PinnedWindowPresentation.ShadeInset * 2 + 1
                        : 120) ||
                    !double.IsFinite(entry.Height) || entry.Height > 32768 ||
                    entry.Height < (entry.Thumbnail is { Enabled: true }
                        ? PinnedWindowPresentation.ShadeInset * 2 + 1
                        : 72) ||
                    !double.IsFinite(entry.AnnotationScaling) || entry.AnnotationScaling is <= 0 or > 16 ||
                    entry.FileListMaximumSize is { IsValid: false } ||
                    entry.Thumbnail is { IsValid: false } || entry.ThumbnailRequest is { IsValid: false })
                {
                    RejectSessionRecord("documents.json", element);
                    rejected++;
                    continue;
                }

                if (_documentWindows.Any(window => window.Id == entry.Window.Id) ||
                    _closedDocuments.Any(closed => closed.State.Window.Id == entry.Window.Id))
                {
                    _unrestoredDocuments.Remove(entry.Window.Id);
                    continue;
                }

                try
                {
                    if (entry.Document is PinnedHtmlDocument html) _ = PinnedRichParser.ReadHtml(html.Html);
                    if (entry.Annotations is { } annotations) ValidateStoredDocumentAnnotations(annotations);
                    entry.Annotations = await ReadAnnotationImagesAsync(entry.Annotations, token);
                }
                catch (Exception error) when (error is PinnedHtmlFormatException or InvalidDataException or
                                                  IOException or UnauthorizedAccessException)
                {
                    RejectSessionRecord("documents.json", element);
                    PinLogger.ZLogWarning(
                        $"Stored document content was rejected before restoration. ErrorType={error.GetType().Name} Code={error.HResult}");
                    rejected++;
                    continue;
                }

                if (entry.ClosedAtUtcTicks > 0)
                {
                    _closedDocuments.Add((entry, entry.ClosedAtUtcTicks));
                    continue;
                }

                _unrestoredDocuments[entry.Window.Id] = entry;
                if (!restore) continue;
                PinnedDocumentWindow? window = null;
                try
                {
                    window = CreateDocumentWindow(entry.Document);
                    window.RestoreDocumentState(entry);
                    MaskRestoredWindow(window, entry.Window);
                    if (entry.Window.Group != Preferences.ActiveGroup)
                    {
                        var visible = entry.Window.GroupHidden ? entry.Window.VisibleInGroup : entry.Window.Visible;
                        window.Presentation.HideForGroup(visible);
                        entry.Window.GroupHidden = true;
                        entry.Window.VisibleInGroup = visible;
                        entry.Window.Visible = false;
                    }

                    if (AllPinsHidden || entry.Window.Group != Preferences.ActiveGroup ||
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

        private async Task<PinnedDocumentState[]> WriteDocumentsAsync(CancellationToken token)
        {
            var saved = _documentWindows.Select(window =>
                {
                    var state = _pendingDocuments.TryGetValue(window, out var pending)
                        ? pending with { Window = pending.Window.CopyWindowState() }
                        : window.CaptureDocumentState();
                    CaptureMaskVisibility(window, state.Window);
                    return state;
                })
                .Concat(_unrestoredDocuments.Values)
                .Concat(_closedDocuments.Select(item => item.State with
                {
                    Window = item.State.Window.CopyWindowState(), ClosedAtUtcTicks = item.ClosedAt,
                })).ToArray();
            Directory.CreateDirectory(_sessionDirectory!);
            var path = Path.Combine(_sessionDirectory!, "documents.json");
            var staging = path + ".tmp";
            var createdImages = new List<string>();
            var committed = false;
            try
            {
                foreach (var state in saved)
                    await WriteAnnotationImagesAsync(state.Annotations, createdImages, token);
                await using (var stream = File.Create(staging))
                    await JsonSerializer.SerializeAsync(stream, saved,
                        PinnedImageJsonContext.Default.PinnedDocumentStateArray, token);
                token.ThrowIfCancellationRequested();
                if (new FileInfo(staging).Length > MaximumSessionBytes)
                    throw new InvalidDataException(UiText.Get("PinnedImageSessionTooLarge"));
                File.Move(staging, path, true);
                committed = true;
                return saved;
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
                if (!committed)
                    foreach (var resource in createdImages)
                        try
                        {
                            File.Delete(resource);
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        {
                            PinLogger.ZLogWarning(error, $"Pinned document annotation resource rollback failed.");
                        }
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
