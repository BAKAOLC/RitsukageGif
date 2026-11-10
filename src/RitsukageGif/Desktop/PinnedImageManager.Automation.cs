using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        internal void RestoreAutomationInteraction()
        {
            if (AllWindows.All(window => window.Group != Preferences.ActiveGroup) &&
                AllWindows.FirstOrDefault() is { } pin)
                SetActiveGroup(pin.Group);
            RestoreInteraction();
            RefreshPresentation();
        }

        internal IPinAutomationService CreateAutomationService()
        {
            return new AutomationService(this);
        }

        private static PinInfo AutomationInfo(IPinnedWindow pin)
        {
            var window = pin.Window;
            var zoom = pin switch
            {
                PinnedImageWindow image => image.AutomationZoom,
                PinnedDocumentWindow document => document.AutomationZoom,
                _ => throw new NotSupportedException(),
            };
            return new(pin.Id, AutomationKind(pin), pin.Group,
                pin.Presentation.Title.Value, window.Position.X, window.Position.Y,
                checked((int)Math.Ceiling(window.Bounds.Width * window.RenderScaling)),
                checked((int)Math.Ceiling(window.Bounds.Height * window.RenderScaling)),
                zoom, window.Opacity, window.IsVisible && window.WindowState != WindowState.Minimized, window.Topmost,
                pin.IsLocked, pin.IsMouseThrough,
                pin.CanSetMouseThrough,
                pin is PinnedImageWindow thumbnailImage
                    ? thumbnailImage.AutomationThumbnail
                    : ((PinnedDocumentWindow)pin).AutomationThumbnail,
                pin.Presentation.AutomationShadow,
                pin is PinnedImageWindow markedImage
                    ? markedImage.AutomationAnnotationsVisible
                    : ((PinnedDocumentWindow)pin).AutomationAnnotationsVisible,
                (pin as PinnedDocumentWindow)?.AutomationSelectable,
                (pin as PinnedDocumentWindow)?.AutomationIgnoreFormat,
                pin is PinnedImageWindow scrolledImage
                    ? scrolledImage.AutomationScrollX
                    : ((PinnedDocumentWindow)pin).AutomationScrollX,
                pin is PinnedImageWindow scrolledYImage
                    ? scrolledYImage.AutomationScrollY
                    : ((PinnedDocumentWindow)pin).AutomationScrollY,
                (pin as PinnedImageWindow)?.AutomationTransparentLayer,
                pin is PinnedImageWindow { AutomationCanIgnoreTransparentInput: true });
        }

        private static string AutomationKind(IPinnedWindow pin)
        {
            return pin switch
            {
                PinnedImageWindow { LiveCaptureActive: true } => "live",
                PinnedImageWindow { AnimationActive: true } => "animation",
                PinnedImageWindow => "image",
                PinnedDocumentWindow document => AutomationDocumentKind(document.Document),
                _ => throw new NotSupportedException(),
            };
        }

        private static string AutomationDocumentKind(PinnedDocument document)
        {
            return document switch
            {
                PinnedTextDocument => "text", PinnedHtmlDocument => "html", PinnedColorDocument => "color",
                PinnedFilesDocument => "files", PinnedFormulaDocument => "formula", _ => "document",
            };
        }

        private IPinnedWindow AutomationFind(string id)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return AllWindows.FirstOrDefault(pin => pin.Id == id) ??
                   throw new KeyNotFoundException("The pin was not found: " + id);
        }

        private PinGroupInfo AutomationGroup(PinnedGroupDefinition group)
        {
            var counts = GetGroupPinCounts(group.Id);
            return new(group.Id, group.Name, group.Color, group.Id == Preferences.ActiveGroup,
                counts.Visible, counts.Total);
        }

        private PinInfo AutomationSet(string id, PinUpdate update)
        {
            ArgumentNullException.ThrowIfNull(update);
            var pin = AutomationFind(id);
            if ((update.Zoom is { } zoom && (!double.IsFinite(zoom) || zoom <= 0)) ||
                (update.Opacity is { } opacity && (!double.IsFinite(opacity) || opacity is < 0 or > 1)) ||
                update.Title is { Length: > 32767 })
                throw new ArgumentOutOfRangeException(nameof(update));
            if (update.Group is { } group && GroupDefinitions.All(value => value.Id != group))
                throw new KeyNotFoundException("The pin group was not found: " + group);
            if (update.Visible is true && (update.Group ?? pin.Group) != Preferences.ActiveGroup)
                throw new InvalidOperationException("Activate the pin group before showing this pin.");
            switch (update.MouseThrough)
            {
                case true when !pin.CanSetMouseThrough:
                    throw new NotSupportedException("Mouse-through is not supported for this pin.");
                case true when !CanRestoreHidden():
                    throw new InvalidOperationException(UiText.Get("PinnedImageRestoreRequired"));
            }

            if (update.Thumbnail is not null && pin is PinnedImageWindow { LiveCaptureActive: true })
                throw new NotSupportedException("Live pins do not support thumbnail display.");
            if (update.Thumbnail.HasValue && ((pin.IsInteractionLocked && update.Locked is not false) ||
                                              pin is PinnedImageWindow { AutomationCanHideAnnotations: false } or
                                                  PinnedDocumentWindow { AutomationCanHideAnnotations: false }))
                throw new InvalidOperationException("The pin cannot change thumbnail display while locked or editing.");
            if (update.AnnotationsVisible.HasValue &&
                pin is PinnedImageWindow { AutomationCanHideAnnotations: false } or
                    PinnedDocumentWindow { AutomationCanHideAnnotations: false })
                throw new InvalidOperationException("Finish interactive annotation editing first.");
            if ((update.Selectable.HasValue && pin is not PinnedDocumentWindow { AutomationSelectable: not null }) ||
                (update.IgnoreFormat.HasValue && pin is not PinnedDocumentWindow { AutomationIgnoreFormat: not null }))
                throw new NotSupportedException("The pin does not support the requested text option.");
            if ((update.ScrollX is { } scrollX && (!double.IsFinite(scrollX) || scrollX < 0)) ||
                (update.ScrollY is { } scrollY && (!double.IsFinite(scrollY) || scrollY < 0)))
                throw new ArgumentOutOfRangeException(nameof(update));
            if (update.TransparentLayer is { } layer &&
                (pin is not PinnedImageWindow { AutomationTransparentLayer: not null } ||
                 !Enum.IsDefined(layer) || (layer == PinTransparentLayer.IgnoreMouse &&
                                            pin is not PinnedImageWindow
                                            {
                                                AutomationCanIgnoreTransparentInput: true,
                                            })))
                throw new NotSupportedException("The pin does not support the requested transparent layer mode.");
            var geometry = update.X.HasValue || update.Y.HasValue || update.Zoom.HasValue || update.ScrollX.HasValue ||
                           update.ScrollY.HasValue;
            if (geometry && pin.IsInteractionLocked && update.Locked is not false)
                throw new InvalidOperationException("The pin is locked.");
            if (update.Zoom.HasValue && pin is PinnedImageWindow { AutomationCanResize: false } &&
                !pin.IsInteractionLocked)
                throw new InvalidOperationException("The pin cannot be resized while editing.");
            if (update.Locked is false) pin.SetLocked(false);
            if (update.Zoom is { } scale)
                switch (pin)
                {
                    case PinnedImageWindow image: image.SetAutomationZoom(scale); break;
                    case PinnedDocumentWindow document: document.SetAutomationZoom(scale); break;
                }

            if (update.X.HasValue || update.Y.HasValue)
                pin.Window.Position = new(update.X ?? pin.Window.Position.X, update.Y ?? pin.Window.Position.Y);
            if (update.ScrollX.HasValue || update.ScrollY.HasValue)
                switch (pin)
                {
                    case PinnedImageWindow image: image.SetAutomationScroll(update.ScrollX, update.ScrollY); break;
                    case PinnedDocumentWindow document:
                        document.SetAutomationScroll(update.ScrollX, update.ScrollY); break;
                }

            if (update.TransparentLayer is { } mode) ((PinnedImageWindow)pin).SetAutomationTransparentLayer(mode);
            if (update.Title is { } title) pin.Presentation.Title.SetTitle(title);
            if (update.Topmost is { } topmost)
                switch (pin)
                {
                    case PinnedImageWindow image: image.SetAutomationTopmost(topmost); break;
                    case PinnedDocumentWindow document: document.SetAutomationTopmost(topmost); break;
                }

            if (update.MouseThrough is { } through) pin.SetMouseThrough(through);
            if (update.Opacity is { } alpha) pin.Window.Opacity = alpha;
            if (update.Thumbnail is { } thumbnail) pin.SetThumbnailDisplay(thumbnail);
            if (update.Shadow is { } shadow) pin.Presentation.SetAutomationShadow(shadow);
            if (update.AnnotationsVisible is { } annotationsVisible)
                switch (pin)
                {
                    case PinnedImageWindow image: image.SetAutomationAnnotationsVisible(annotationsVisible); break;
                    case PinnedDocumentWindow document:
                        document.SetAutomationAnnotationsVisible(annotationsVisible); break;
                }

            if (pin is PinnedDocumentWindow textDocument)
                textDocument.SetAutomationTextOptions(update.Selectable, update.IgnoreFormat);
            if (update.Group is { } destination)
            {
                pin.SetGroup(destination);
                switch (pin)
                {
                    case PinnedImageWindow image: _pendingRestore.Remove(image); break;
                    case PinnedDocumentWindow document when _pendingDocuments.Remove(document, out var saved):
                        document.RestoreDocumentState(saved);
                        break;
                }

                if (destination == Preferences.ActiveGroup) pin.Presentation.RevealGroup();
                else pin.Presentation.HideForGroup();
            }

            if (update.Visible is { } visible)
            {
                if (visible)
                {
                    RevealWindow(pin);
                }
                else
                {
                    pin.Presentation.SetGroupIntent(false);
                    switch (pin)
                    {
                        case PinnedImageWindow image when _pendingRestore.TryGetValue(image, out var pending):
                            pending.Visible = false;
                            pending.VisibleInGroup = false;
                            break;
                        case PinnedDocumentWindow document when _pendingDocuments.TryGetValue(document, out var saved):
                            saved.Window.Visible = false;
                            saved.Window.VisibleInGroup = false;
                            break;
                    }

                    pin.HideImage();
                }
            }

            if (update.Locked is true) pin.SetLocked(true);
            RefreshPendingWindowState(pin);
            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
            PinLogger.ZLogInformation($"Pin updated by script. PinId={id}");
            return AutomationInfo(pin);
        }

        private sealed partial class AutomationService(PinnedImageManager manager) : IPinAutomationService
        {
            public Task<IReadOnlyList<PinInfo>> ListAsync(CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    PinInfo[] pins = [.. manager.AllWindows.Select(AutomationInfo)];
                    return Task.FromResult<IReadOnlyList<PinInfo>>(pins);
                }, token);
            }

            public Task<PinInfo?> GetAsync(string id, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    var pin = manager.AllWindows.FirstOrDefault(value => value.Id == id);
                    return Task.FromResult(pin is null ? null : AutomationInfo(pin));
                }, token);
            }

            public Task<PinInfo> PinFileAsync(string path, CancellationToken token = default)
            {
                return OnUi(async () =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    path = Path.GetFullPath(path);
                    var kinds = manager.Preferences.SupportedContentKinds;
                    IPinnedWindow? pin;
                    if ((kinds & PinnedContentKinds.Images) != 0 &&
                        await manager.TryPrepareFileImageAsync([path], token) is { } image)
                    {
                        token.ThrowIfCancellationRequested();
                        pin = manager.Pin(image, null, "") ?? manager._windows.FirstOrDefault(window =>
                            window.Group == manager.Preferences.ActiveGroup && window.ContentHash == image.ContentHash);
                    }
                    else
                    {
                        var snapshot = new PinnedClipboardSnapshot([], null, [path], null, null);
                        var document = PinnedContentRegistry.Create(snapshot, kinds) ??
                                       throw new NotSupportedException(
                                           "The file cannot be pinned with the current supported content settings.");
                        token.ThrowIfCancellationRequested();
                        pin = manager.PinDocument(document) ?? manager._documentWindows.FirstOrDefault(window =>
                            window.Group == manager.Preferences.ActiveGroup && SameDocument(window.Document, document));
                    }

                    return AutomationInfo(
                        pin ?? throw new InvalidOperationException("The pin was not created."));
                }, token);
            }

            public Task<PinInfo> SetAsync(string id, PinUpdate update, CancellationToken token = default)
            {
                return OnUi(() => Task.FromResult(manager.AutomationSet(id, update)), token);
            }

            public Task<PinInfo> FitAsync(string id, int width, int height, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
                    var pin = manager.AutomationFind(id);
                    switch (pin)
                    {
                        case PinnedImageWindow image: image.FitAutomation(width, height); break;
                        case PinnedDocumentWindow document: document.FitAutomation(width, height); break;
                    }

                    manager.ScheduleSave();
                    return Task.FromResult(AutomationInfo(pin));
                }, token);
            }

            public Task CloseAsync(string id, bool destroy = false, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    var pin = manager.AutomationFind(id);
                    if (pin.IsCloseLocked) throw new InvalidOperationException("The pin is locked against closing.");
                    pin.CloseImage(destroy);
                    return Task.FromResult(true);
                }, token);
            }

            public Task<PinInfo?> RestoreAsync(string? id = null, CancellationToken token = default)
            {
                return OnUi<PinInfo?>(async () =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    if (id is not null)
                    {
                        if (manager.AllWindows.FirstOrDefault(window => window.Id == id) is { } open)
                            return AutomationInfo(open);
                        if (manager._closedImages.FirstOrDefault(entry => entry.State.Id == id) is { } image)
                        {
                            if (image.State.Group != manager.Preferences.ActiveGroup)
                                throw new InvalidOperationException(
                                    "Activate the pin group before restoring this pin.");
                            await manager.RestoreClosedAsync(image, token);
                        }
                        else
                        {
                            var index = manager._closedDocuments.FindIndex(entry => entry.State.Window.Id == id);
                            if (index < 0) return null;
                            if (manager._closedDocuments[index].State.Window.Group != manager.Preferences.ActiveGroup)
                                throw new InvalidOperationException(
                                    "Activate the pin group before restoring this pin.");
                            token.ThrowIfCancellationRequested();
                            manager.RestoreClosedDocument(index);
                        }

                        var restored = manager.AllWindows.FirstOrDefault(window => window.Id == id);
                        return restored is null ? null : AutomationInfo(restored);
                    }

                    var before = manager.AllWindows.Select(window => window.Id).ToHashSet(StringComparer.Ordinal);
                    if (!await manager.RestoreLastClosedAsync(token)) return null;
                    var result = manager.AllWindows.FirstOrDefault(window => !before.Contains(window.Id));
                    return result is null ? null : AutomationInfo(result);
                }, token);
            }

            public Task<IReadOnlyList<PinClosedInfo>> ClosedAsync(CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    var result = manager._closedImages.Select(entry => new PinClosedInfo(entry.State.Id,
                        entry.State.LiveSource is not null ? "live" :
                        entry.State.AnimationActive ? "animation" : "image",
                        entry.State.Group, entry.State.CustomTitle)).Concat(manager._closedDocuments.Select(entry =>
                        new PinClosedInfo(entry.State.Window.Id, AutomationDocumentKind(entry.State.Document),
                            entry.State.Window.Group,
                            entry.State.Window.CustomTitle))).ToArray();
                    return Task.FromResult<IReadOnlyList<PinClosedInfo>>(result);
                }, token);
            }

            public Task<IReadOnlyList<PinGroupInfo>> GroupsAsync(CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    PinGroupInfo[] groups = [.. manager.GroupDefinitions.Select(manager.AutomationGroup)];
                    return Task.FromResult<IReadOnlyList<PinGroupInfo>>(groups);
                }, token);
            }

            public Task<PinGroupInfo> CreateGroupAsync(string name, string? color = null,
                CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    var group = new PinnedGroupDefinition
                        { Name = name, Color = string.IsNullOrEmpty(color) ? null : color };
                    manager.CreateGroup(group, false);
                    return Task.FromResult(
                        manager.AutomationGroup(manager.GroupDefinitions.Single(value => value.Id == group.Id)));
                }, token);
            }

            public Task<PinGroupInfo> SetGroupAsync(string id, string? name = null, string? color = null,
                CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    var previous = manager.GroupDefinitions.FirstOrDefault(group => group.Id == id) ??
                                   throw new KeyNotFoundException("The pin group was not found: " + id);
                    manager.UpdateGroup(id, previous with
                    {
                        Name = name ?? previous.Name,
                        Color = color is null ? previous.Color : color.Length == 0 ? null : color,
                    });
                    return Task.FromResult(
                        manager.AutomationGroup(manager.GroupDefinitions.Single(value => value.Id == id)));
                }, token);
            }

            public Task ActivateGroupAsync(string id, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    if (manager.GroupDefinitions.All(group => group.Id != id))
                        throw new KeyNotFoundException("The pin group was not found: " + id);
                    manager.SetActiveGroup(id);
                    return Task.FromResult(true);
                }, token);
            }

            public Task RemoveGroupAsync(string id, string? moveTo = null, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    ObjectDisposedException.ThrowIf(manager._disposed, manager);
                    manager.RemoveGroup(id, moveTo);
                    return Task.FromResult(true);
                }, token);
            }

            private static Task<T> OnUi<T>(Func<Task<T>> action, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (Dispatcher.UIThread.CheckAccess()) return action();
                return Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    return await action();
                }).WaitAsync(token);
            }
        }
    }
}
