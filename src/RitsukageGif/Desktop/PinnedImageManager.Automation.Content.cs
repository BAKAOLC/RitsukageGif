namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private PinInfo AutomationDocument(PinnedClipboardSnapshot snapshot, PinnedContentKinds kind)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if ((Preferences.SupportedContentKinds & kind) == 0)
                throw new NotSupportedException("This pin content type is disabled.");
            var document = PinnedContentRegistry.Create(snapshot, kind) ??
                           throw new ArgumentException("The content cannot be pinned.", nameof(snapshot));
            var created = PinDocument(document);
            created?.Presentation.SourceMime = PinnedSourceMime.FromDocument(document);
            var pin = created ?? _documentWindows.FirstOrDefault(window => window.Group == Preferences.ActiveGroup &&
                                                                           SameDocument(window.Document, document));
            return AutomationInfo(pin ?? throw new InvalidOperationException("The pin was not created."));
        }

        private static Task<ScreenshotResult> AutomationCapture(IPinnedWindow pin, PinOutputView view,
            OutputColorMode mode, CancellationToken token)
        {
            return pin switch
            {
                PinnedImageWindow image => image.CaptureAutomationAsync(view, mode, token),
                PinnedDocumentWindow document => document.CaptureAutomationAsync(view, mode, token),
                _ => throw new NotSupportedException(),
            };
        }

        private sealed partial class AutomationService
        {
            public Task<bool> TranslationAsync(string id, bool? enabled = null, CancellationToken token = default)
            {
                return OnUi(() => manager.AutomationFind(id) switch
                {
                    PinnedImageWindow image => image.TranslationAutomationAsync(enabled, token),
                    PinnedDocumentWindow document => document.TranslationAutomationAsync(enabled, token),
                    _ => throw new NotSupportedException("This pin does not support inline translation."),
                }, token);
            }

            public Task<PinInfo> CreateImageAsync(ReadOnlyMemory<byte> image, CancellationToken token = default)
            {
                return OnUi(async () =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    if ((manager.Preferences.SupportedContentKinds & PinnedContentKinds.Images) == 0)
                        throw new NotSupportedException("Image pins are disabled.");
                    if (image.IsEmpty || image.Length > PinnedImageContent.MaximumEncodedBytes)
                        throw new ArgumentOutOfRangeException(nameof(image));
                    var content = await manager.TryPrepareClipboardAsync(
                                      () => Task.Run<PinnedImageContent?>(() => PinnedImageContent.Decode(image, token),
                                          token), token) ??
                                  throw new InvalidDataException("The image could not be decoded.");
                    token.ThrowIfCancellationRequested();
                    var pin = manager.Pin(content, null, "") ?? manager._windows.FirstOrDefault(window =>
                        window.Group == manager.Preferences.ActiveGroup && window.ContentHash == content.ContentHash);
                    return AutomationInfo(
                        pin ?? throw new InvalidOperationException("The pin was not created."));
                }, token);
            }

            public Task<PinInfo> CreateTextAsync(string text, CancellationToken token = default)
            {
                return OnUi(() => Task.FromResult(manager.AutomationDocument(new([], null, [], text, null),
                    PinnedContentKinds.Text)), token);
            }

            public Task<PinInfo> CreateHtmlAsync(string html, string? text = null, CancellationToken token = default)
            {
                return OnUi(() => Task.FromResult(manager.AutomationDocument(new([], null, [], text, html),
                    PinnedContentKinds.Text)), token);
            }

            public Task<PinInfo> CreateColorAsync(string color, CancellationToken token = default)
            {
                return OnUi(() => Task.FromResult(manager.AutomationDocument(new([], null, [], color, null),
                    PinnedContentKinds.Color)), token);
            }

            public Task<PinInfo> DuplicateAsync(string id, CancellationToken token = default)
            {
                return OnUi(async () =>
                {
                    var source = manager.AutomationFind(id);
                    IPinnedWindow copy;
                    switch (source)
                    {
                        case PinnedImageWindow image:
                        {
                            var state = image.CaptureState();
                            var original = image.OriginalContent;
                            var currentPng = image.CurrentPng;
                            var content = await Task.Run(() => PinnedImageContent.Decode(currentPng, token), token);
                            token.ThrowIfCancellationRequested();
                            manager.AutomationFind(id);
                            content = content with
                            {
                                OriginalEncoded = original.Encoded, Animation = original.Animation,
                                AnimationActive = image.StoredAnimationActive,
                            };
                            var clone = PinnedImageWindow.CreatePrepared(content, () => manager.CanRestoreHidden(),
                                manager.Preferences);
                            state.Id = clone.Id;
                            clone.RestoreOriginal(original);
                            manager.AttachWindow(clone);
                            try
                            {
                                manager.ShowRestoredWindow(clone, state);
                            }
                            catch
                            {
                                manager._windows.Remove(clone);
                                clone.Dispose();
                                throw;
                            }

                            copy = clone;
                            break;
                        }
                        case PinnedDocumentWindow document:
                        {
                            var captured = document.CaptureDocumentState();
                            var state = captured with { Window = captured.Window.CopyWindowState() };
                            var clone = manager.CreateDocumentWindow(state.Document);
                            state.Window.Id = clone.Id;
                            try
                            {
                                clone.RestoreDocumentState(state);
                                manager.MaskRestoredWindow(clone, state.Window);
                                if (state.Window.Group != manager.Preferences.ActiveGroup)
                                    clone.Presentation.HideForGroup(state.Window.VisibleInGroup);
                                if (manager.AllPinsHidden || state.Window.Group != manager.Preferences.ActiveGroup ||
                                    !state.Window.Visible || !manager.CanRestoreNow())
                                {
                                    manager._pendingDocuments.Add(clone, state);
                                }
                                else
                                {
                                    clone.ShowActivated = false;
                                    clone.Show();
                                    clone.RestoreDocumentState(state);
                                }
                            }
                            catch
                            {
                                manager._documentWindows.Remove(clone);
                                manager._pendingDocuments.Remove(clone);
                                clone.Dispose();
                                throw;
                            }

                            copy = clone;
                            break;
                        }
                        default: throw new NotSupportedException();
                    }

                    manager.ScheduleSave();
                    manager.Changed?.Invoke(manager, EventArgs.Empty);
                    return AutomationInfo(copy);
                }, token);
            }

            public Task<PinInfo> CreateFilesAsync(IReadOnlyList<string> paths, CancellationToken token = default)
            {
                return OnUi(() => Task.FromResult(manager.AutomationDocument(
                        new([], null, [.. paths.Select(Path.GetFullPath)], null, null), PinnedContentKinds.Files)),
                    token);
            }

            public Task<PinInfo> CreateFormulaAsync(string latex, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    if ((manager.Preferences.SupportedContentKinds & PinnedContentKinds.Formula) == 0)
                        throw new NotSupportedException("Formula pins are disabled.");
                    if (string.IsNullOrWhiteSpace(latex) || latex.Length > PinnedContentRegistry.MaximumTextCharacters)
                        throw new ArgumentException("A nonempty formula within the text size limit is required.",
                            nameof(latex));
                    var document = new PinnedFormulaDocument([latex]);
                    var pin = manager.PinDocument(document) ?? manager._documentWindows.FirstOrDefault(window =>
                        window.Group == manager.Preferences.ActiveGroup && SameDocument(window.Document, document));
                    return Task.FromResult(
                        AutomationInfo(pin ?? throw new InvalidOperationException("The pin was not created.")));
                }, token);
            }

            public Task<PinSnapshot> SnapshotAsync(string id, PinOutputView view = PinOutputView.Preferred,
                OutputColorMode color = OutputColorMode.Sdr, CancellationToken token = default)
            {
                return OnUi(async () =>
                {
                    var output = await AutomationCapture(manager.AutomationFind(id), view, color, token);
                    ReadOnlyMemory<byte>? hdr = output.ColorMode != OutputColorMode.Sdr && output.Hdr is { } source
                        ? await HdrImageService.EncodePngAsync(source, token)
                        : null;
                    return new PinSnapshot(output.Png, hdr, output.ColorMode);
                }, token);
            }

            public Task<IReadOnlyList<string>> SaveAsync(string id, string path,
                PinOutputView view = PinOutputView.Preferred, OutputColorMode color = OutputColorMode.Sdr,
                CancellationToken token = default)
            {
                return OnUi(() => manager.AutomationFind(id) switch
                {
                    PinnedImageWindow image => image.SaveAutomationAsync(path, view, color, token),
                    PinnedDocumentWindow document => document.SaveAutomationAsync(path, view, color, token),
                    _ => throw new NotSupportedException(),
                }, token);
            }

            public Task CopyAsync(string id, PinCopyKind kind = PinCopyKind.Image,
                PinOutputView view = PinOutputView.Preferred, CancellationToken token = default)
            {
                return OnUi(async () =>
                {
                    switch (manager.AutomationFind(id))
                    {
                        case PinnedImageWindow image: await image.CopyAutomationAsync(kind, view, token); break;
                        case PinnedDocumentWindow document:
                            await document.CopyAutomationAsync(kind, view, token); break;
                    }

                    return true;
                }, token);
            }

            public Task<string?> TextAsync(string id, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    var pin = manager.AutomationFind(id);
                    return Task.FromResult(pin is PinnedDocumentWindow document
                        ? document.Document.PlainText
                        : pin.Presentation.SourceMime?.Text);
                }, token);
            }

            public Task<string?> HtmlAsync(string id, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    var pin = manager.AutomationFind(id);
                    var html = (pin as PinnedDocumentWindow)?.Document switch
                    {
                        PinnedHtmlDocument document => document.Html,
                        PinnedColorDocument { Html.Length: > 0 } color => color.Html,
                        _ => pin.Presentation.SourceMime?.Html,
                    };
                    return Task.FromResult(html);
                }, token);
            }

            public Task<PinInfo> TransformAsync(string id, PinTransform transform, CancellationToken token = default)
            {
                return OnUi(async () =>
                {
                    var pin = manager.AutomationFind(id) as PinnedImageWindow ??
                              throw new NotSupportedException("This pin does not support image transforms.");
                    await pin.TransformAutomationAsync(transform, token);
                    return AutomationInfo(pin);
                }, token);
            }

            public Task<PinInfo> AdjustBrightnessAsync(string id, double delta, CancellationToken token = default)
            {
                return OnUi(async () =>
                {
                    var pin = manager.AutomationFind(id) as PinnedImageWindow ??
                              throw new NotSupportedException("This pin does not support image brightness adjustment.");
                    await pin.AdjustAutomationBrightnessAsync(delta, token);
                    return AutomationInfo(pin);
                }, token);
            }

            public Task<PinInfo> ResetProcessingAsync(string id, CancellationToken token = default)
            {
                return OnUi(async () =>
                {
                    var pin = manager.AutomationFind(id) as PinnedImageWindow ??
                              throw new NotSupportedException("This pin does not support image processing reset.");
                    await pin.ResetAutomationProcessingAsync(token);
                    return AutomationInfo(pin);
                }, token);
            }

            public Task<PinInfo> SetThumbnailRegionAsync(string id, PixelRect region, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    var pin = manager.AutomationFind(id);
                    switch (pin)
                    {
                        case PinnedImageWindow image: image.SetAutomationThumbnailRegion(region); break;
                        case PinnedDocumentWindow document: document.SetAutomationThumbnailRegion(region); break;
                    }

                    return Task.FromResult(AutomationInfo(pin));
                }, token);
            }
        }
    }
}
