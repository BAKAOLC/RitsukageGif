using System.Text.Json;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private const long MaximumSessionBytes = 64L * 1024 * 1024;
        private readonly Dictionary<PinnedImageWindow, PinnedImageState> _pendingRestore = [];
        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly SemaphoreSlim _sessionGate = new(1);

        private readonly Dictionary<string, (ReadOnlyMemory<byte> Png, PinnedImageOriginal Original, string ImageId)>
            _sessionImageReferences = new(StringComparer.Ordinal);

        private readonly Dictionary<string, PinnedImageState> _unrestoredStates = new(StringComparer.Ordinal);
        private PinnedRejectedSession _rejectedSession = new();
        private bool _restoring;
        private bool _saveTimerConfigured;
        private string? _sessionDirectory;
        private bool _sessionTouched;
        public Func<bool> CanRestoreNow { get; set; } = () => true;
        public Action<Exception>? SessionError { get; set; }

        public async Task<int> ConfigureSessionAsync(string directory, bool restore, CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessionDirectory is not null) throw new InvalidOperationException();
            PinLogger.ZLogDebug($"Pinned image session configuration started.");
            var fullDirectory = Path.GetFullPath(directory);
            var recovery =
                await PinnedSessionRecovery.PreserveUnsupportedAsync(fullDirectory, MaximumSessionBytes, token);
            var rejectedSession = await PinnedRejectedSession.ReadAsync(fullDirectory, MaximumSessionBytes, token);
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _sessionDirectory = fullDirectory;
            _rejectedSession = rejectedSession;
            await ReadVisibilityMaskAsync(token);
            if (recovery is not null)
            {
                PinLogger.ZLogWarning($"Unsupported pin session preserved. RecoveryDirectory={recovery}");
                SessionError?.Invoke(new InvalidDataException(UiText.Format("PinnedSessionRecovered", recovery)));
            }

            if (!_saveTimerConfigured)
            {
                _saveTimer.Tick += SaveTimerTick;
                _saveTimerConfigured = true;
            }

            if (restore) return await RestoreSessionCoreAsync(null, token, true);
            await _sessionGate.WaitAsync(token);
            try
            {
                var saved = await ReadStatesAsync(token);
                foreach (var entry in saved.States)
                    if (entry.ClosedAtUtcTicks == 0)
                        _unrestoredStates.TryAdd(entry.Id, entry);

                PinLogger.ZLogDebug(
                    $"Pinned image session configuration completed. DeferredCount={_unrestoredStates.Count} RejectedCount={saved.Rejected}");
                var rejected = saved.Rejected + await ReadClosedImagesAsync(saved.States, token);
                rejected += await ReadDocumentsAsync(false, token, loadClosed: true);
                TrimClosedHistory();
                if (rejected > 0) ScheduleSave();
                return rejected;
            }
            finally
            {
                _sessionGate.Release();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task<(PinnedImageState[] States, int Rejected)> ReadStatesAsync(CancellationToken token)
        {
            var path = Path.Combine(_sessionDirectory!, "session.json");
            if (!File.Exists(path)) return ([], 0);
            try
            {
                if (new FileInfo(path).Length > MaximumSessionBytes) return ([], 1);
                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return ([], 1);
                var states = new List<PinnedImageState>();
                var rejected = 0;
                foreach (var element in document.RootElement.EnumerateArray())
                    try
                    {
                        var state = element.Deserialize(PinnedImageJsonContext.Default.PinnedImageState);
                        if (state?.Annotations is { } annotations)
                            ScreenshotAnnotationSession.ValidateDocument(annotations);
                        if (state is null || state.ClosedAtUtcTicks < 0 ||
                            state.ClosedAtUtcTicks > DateTime.MaxValue.Ticks || !ValidState(state))
                        {
                            RejectSessionRecord("session.json", element);
                            rejected++;
                        }
                        else
                        {
                            states.Add(state);
                        }
                    }
                    catch (Exception error) when (error is JsonException or InvalidDataException)
                    {
                        PinLogger.ZLogWarning(error, $"Pin session record validation failed. Manifest=session.json");
                        RejectSessionRecord("session.json", element);
                        rejected++;
                    }

                return ([.. states], rejected);
            }
            catch (Exception error) when (error is JsonException or IOException)
            {
                PinLogger.ZLogWarning(error, $"Pinned image session manifest could not be read.");
                return ([], 1);
            }
        }

        private void RejectSessionRecord(string manifest, JsonElement record)
        {
            if (!_rejectedSession.Add(manifest, record)) return;
            var path = Path.Combine(_sessionDirectory!, "rejected-session.json");
            PinLogger.ZLogWarning($"Pin session record quarantined. Manifest={manifest} QuarantinePath={path}");
        }

        private void ScheduleSave()
        {
            if (_disposed || _restoring || _sessionDirectory is null) return;
            _saveTimer.Stop();
            _sessionTouched = true;
            _saveTimer.Start();
        }

        private void SaveTimerTick(object? sender, EventArgs args)
        {
            _saveTimer.Stop();
            _ = SaveSessionSafelyAsync();
        }

        private async Task SaveSessionSafelyAsync()
        {
            _saveTimer.Stop();
            try
            {
                await FlushSessionAsync();
            }
            catch (Exception error)
            {
                SessionError?.Invoke(error);
            }
        }

        public Task<int> RestoreSessionAsync(CancellationToken token = default)
        {
            return RestoreSessionCoreAsync(null, token);
        }

        private async Task<int> RestoreSessionCoreAsync(string? group, CancellationToken token, bool loadClosed = false)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessionDirectory is null) return 0;
            PinLogger.ZLogDebug($"Pinned image session restore started.");
            await _sessionGate.WaitAsync(token);
            int rejected;
            try
            {
                var (entries, rejectedCount) = await ReadStatesAsync(token);
                rejected = rejectedCount;
                if (loadClosed) rejected += await ReadClosedImagesAsync(entries, token);
                var knownIds = _windows.Select(window => window.Id)
                    .Concat(_pendingRestore.Values.Select(state => state.Id))
                    .Concat(_closedImages.Select(entry => entry.State.Id)).ToHashSet(StringComparer.Ordinal);
                var restoredCount = 0;
                foreach (var state in entries)
                {
                    if (state.ClosedAtUtcTicks != 0 || (group is not null && state.Group != group)) continue;
                    token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);


                    if (knownIds.Contains(state.Id))
                    {
                        _unrestoredStates.Remove(state.Id);
                        continue;
                    }

                    _unrestoredStates[state.Id] = state;
                    PinnedImageWindow? window = null;
                    try
                    {
                        var imagePath = ImagePath(state.ImageId);
                        if (new FileInfo(imagePath).Length > 256L * 1024 * 1024)
                        {
                            rejected++;
                            continue;
                        }

                        var png = await File.ReadAllBytesAsync(imagePath, token);
                        token.ThrowIfCancellationRequested();
                        ObjectDisposedException.ThrowIf(_disposed, this);
                        var content = await Task.Run(() => PinnedImageContent.Decode(png, token), token);
                        var original = await ReadOriginalAsync(state, content, token);
                        state.Annotations = await ReadAnnotationImagesAsync(state.Annotations, token);
                        state.Translation = await ReadTranslationImageAsync(state.Translation, png, token);
                        token.ThrowIfCancellationRequested();
                        content = content with
                        {
                            OriginalEncoded = original.Encoded, Animation = original.Animation,
                            AnimationActive = state.AnimationActive,
                        };
                        window = PinnedImageWindow.CreatePrepared(content, () => CanRestoreHidden(), Preferences);
                        window.RestoreOriginal(original, !state.HasOriginalImage);
                        AttachWindow(window);
                        _restoring = true;
                        try
                        {
                            ShowRestoredWindow(window, state);
                            knownIds.Add(state.Id);
                            _unrestoredStates.Remove(state.Id);
                        }
                        finally
                        {
                            _restoring = false;
                        }

                        if (++restoredCount % 16 == 0)
                            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        window?.Dispose();
                        throw;
                    }
                    catch (Exception error) when (error is not OutOfMemoryException and not ObjectDisposedException)
                    {
                        PinLogger.ZLogWarning(error, $"Pinned image session entry could not be restored.");
                        if (window is not null)
                        {
                            _windows.Remove(window);
                            _pendingRestore.Remove(window);
                            window.StateChanged -= WindowStateChanged;
                            window.Dispose();
                        }

                        rejected++;
                    }
                }

                _restoring = true;
                rejected += await ReadDocumentsAsync(true, token, group, loadClosed);
                if (loadClosed)
                {
                    _restoring = false;
                    TrimClosedHistory();
                }

                if (restoredCount > 0 || rejected > 0)
                    PinLogger.ZLogInformation(
                        $"Pinned image session restore completed. RestoredCount={restoredCount} RejectedCount={rejected}");
                else PinLogger.ZLogDebug($"Pinned image session restore found no new entries.");
            }
            finally
            {
                _restoring = false;
                _sessionGate.Release();
                Changed?.Invoke(this, EventArgs.Empty);
            }

            if (rejected > 0) ScheduleSave();
            return rejected;
        }

        public async Task FlushSessionAsync(CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            _saveTimer.Stop();
            var directory = _sessionDirectory;
            if (directory is null || _disposed || !_sessionTouched) return;
            await _sessionGate.WaitAsync(token);
            try
            {
                await WriteSessionCoreAsync(directory, token);
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        private async Task WriteSessionCoreAsync(string directory, CancellationToken token)
        {
            PinLogger.ZLogDebug($"Pinned image session save started.");
            var createdImages = new List<string>();
            var liveSnapshots = new List<WindowRoiPinSnapshot>();
            var committed = false;
            try
            {
                if (_disposed) return;
                await _rejectedSession.WriteAsync(directory, token);
                var documents = await WriteDocumentsAsync(token);
                var images = _windows.Select(window =>
                {
                    var live = window.CaptureLiveSnapshot();
                    if (live is not null) liveSnapshots.Add(live);
                    var state = _pendingRestore.TryGetValue(window, out var pending)
                        ? pending.Copy()
                        : window.CaptureState();
                    CaptureMaskVisibility(window, state);
                    return (
                        State: state,
                        Png: window.CurrentPng, Original: live is null ? window.OriginalContent : null,
                        window.AnimationActive, Live: live);
                }).Concat(_closedImages.Select(entry =>
                {
                    var live = (entry as PinnedClosedLiveImage)?.Snapshot.Retain();
                    if (live is not null) liveSnapshots.Add(live);
                    var state = entry.State.Copy();
                    state.ClosedAtUtcTicks = entry.ClosedAt;
                    var original = entry as PinnedClosedStaticImage;
                    return (State: state, Png: original?.Png ?? default, original?.Original,
                        AnimationActive: original?.AnimationActive ?? false, Live: live);
                })).ToArray();
                Directory.CreateDirectory(directory);
                await WriteVisibilityMaskAsync(directory, token);
                var savedImages =
                    new Dictionary<string, (ReadOnlyMemory<byte> Png, PinnedImageOriginal Original, string ImageId)>(
                        StringComparer.Ordinal);
                foreach (var (state, originalPng, original, animationActive, live) in images)
                {
                    await WriteAnnotationImagesAsync(state.Annotations, createdImages, token);
                    await WriteTranslationImageAsync(state.Translation, createdImages, token);
                    if (original is not null && _sessionImageReferences.TryGetValue(state.Id, out var saved) &&
                        saved.Png.Equals(originalPng) && saved.Original == original)
                    {
                        state.ImageId = saved.ImageId;
                        state.HasOriginalImage = !originalPng.Equals((ReadOnlyMemory<byte>)original.Png);
                        state.HasOriginalSdrImage = original.Hdr is not null && original.SdrPng is not null;
                        state.HasOriginalEncoded = original.Encoded is not null;
                        state.AnimationActive = animationActive;
                        savedImages.Add(state.Id, saved);
                        continue;
                    }

                    var png = originalPng;
                    if (live is not null)
                    {
                        state.LiveSource = live.Descriptor;
                        png = (await live.Frame.PrepareContentAsync(token)).Png;
                        if (_disposed) return;
                    }

                    state.ImageId = Guid.NewGuid().ToString("N");
                    state.HasOriginalImage = original is not null && !png.Equals((ReadOnlyMemory<byte>)original.Png);
                    state.HasOriginalSdrImage = original is { Hdr: not null, SdrPng: not null };
                    state.HasOriginalEncoded = original?.Encoded is not null;
                    state.AnimationActive = animationActive;
                    var target = ImagePath(state.ImageId);
                    await WriteSessionImageAsync(target, png, createdImages, token);
                    if (original is null) continue;
                    if (state.HasOriginalImage)
                        await WriteSessionImageAsync(OriginalPath(state.ImageId, false), original.Png, createdImages,
                            token);
                    if (state.HasOriginalSdrImage && original.SdrPng is { } sdr)
                        await WriteSessionImageAsync(OriginalPath(state.ImageId, true), sdr, createdImages, token);
                    if (original.Encoded is { } media)
                        await WriteSessionImageAsync(OriginalEncodedPath(state.ImageId), media, createdImages, token);
                    savedImages.Add(state.Id, (originalPng, original, state.ImageId));
                }

                var states = images.Select(image => image.State).Concat(_unrestoredStates.Values).ToArray();
                var manifest = Path.Combine(directory, "session.json");
                var staging = manifest + ".tmp";
                try
                {
                    await using (var stream = File.Create(staging))
                        await JsonSerializer.SerializeAsync(stream, states,
                            PinnedImageJsonContext.Default.PinnedImageStateArray, token);
                    token.ThrowIfCancellationRequested();
                    if (new FileInfo(staging).Length > MaximumSessionBytes)
                        throw new InvalidDataException(UiText.Get("PinnedImageSessionTooLarge"));
                    File.Move(staging, manifest, true);
                    committed = true;
                    _sessionImageReferences.Clear();
                    foreach (var (id, saved) in savedImages) _sessionImageReferences.Add(id, saved);
                }
                finally
                {
                    if (File.Exists(staging)) File.Delete(staging);
                }

                var current = states.Select(state => state.ImageId).ToHashSet(StringComparer.Ordinal);
                current.UnionWith(states.Where(state => state.Translation?.Cache is not null)
                    .Select(state => state.Translation!.Cache!.ImageId));
                CleanupAnnotationImages(states, documents);
                current.UnionWith(_rejectedSession.ImageIds);
                foreach (var path in Directory.EnumerateFiles(directory, "*.png*"))
                {
                    var name = Path.GetFileName(path);
                    if (name.Length < 36 || !Guid.TryParseExact(name.AsSpan(0, 32), "N", out _) ||
                        name[32..] is not (".png" or ".png.original.png" or ".png.original.sdr.png"
                            or ".png.original.media") ||
                        current.Contains(name[..32])) continue;
                    File.Delete(path);
                }

                PinLogger.ZLogInformation(
                    $"Pinned image session save completed. StateCount={states.Length} NewImageCount={createdImages.Count}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                PinLogger.ZLogDebug($"Pinned image session save cancelled.");
                throw;
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Pinned image session save failed.");
                throw;
            }
            finally
            {
                foreach (var snapshot in liveSnapshots) snapshot.Dispose();
                if (!committed)
                    foreach (var path in createdImages)
                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        {
                            PinLogger.ZLogWarning(error, $"Pinned image session rollback failed.");
                        }
            }
        }

        private void ShowRestoredWindow(PinnedImageWindow window, PinnedImageState state)
        {
            window.ShowActivated = false;
            window.SetGroup(state.Group);
            window.Presentation.Restore(state);
            MaskRestoredWindow(window, state);
            if (state.Group != Preferences.ActiveGroup)
            {
                var visible = state.GroupHidden ? state.VisibleInGroup : state.Visible;
                window.Presentation.HideForGroup(visible);
                state.GroupHidden = true;
                state.VisibleInGroup = visible;
                state.Visible = false;
            }

            if (AllPinsHidden || state.Group != Preferences.ActiveGroup ||
                (!state.Visible && CanRestoreHidden()) || !CanRestoreNow())
            {
                window.RestoreHiddenState(state);
                _pendingRestore.Add(window, state);
                window.InitializeTextRecognition();
                return;
            }

            window.Show();
            window.RestoreState(state);
        }

        public void ResumeDeferred()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed || AllPinsHidden || !CanRestoreNow()) return;
            ResumeDocumentWindows();
            ResumeGroupRestore();
            foreach (var (window, state) in _pendingRestore.ToArray())
            {
                if (state.Group != Preferences.ActiveGroup || (!state.Visible && CanRestoreHidden())) continue;
                _pendingRestore.Remove(window);
                window.Show();
                if (!state.Visible) window.HideImage();
            }
        }

        private void RevealWindow(PinnedImageWindow window)
        {
            _pendingRestore.Remove(window);
            window.RevealImage();
        }

        private string ImagePath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException(null, nameof(id));
            return Path.Combine(_sessionDirectory!, id + ".png");
        }

        private bool ValidState(PinnedWindowState? state, bool thumbnail = false)
        {
            return state is { CustomTitle.Length: <= 32767, WindowShape: null or { IsValid: true } } &&
                   (state.Group is "" || Guid.TryParseExact(state.Group, "N", out _)) &&
                   GroupDefinitions.Any(group => group.Id == state.Group) &&
                   Guid.TryParseExact(state.Id, "N", out _) &&
                   double.IsFinite(state.Zoom) && state.Zoom > 0 &&
                   (state.Zoom <= 8 || thumbnail || state is PinnedImageState { Thumbnail: true }) &&
                   double.IsFinite(state.CaptureScaling) && state.CaptureScaling is >= 1 and <= 16 &&
                   double.IsFinite(state.Opacity) && state.Opacity is >= 0 and <= 1 &&
                   double.IsFinite(state.ScrollX) && state.ScrollX >= 0 &&
                   double.IsFinite(state.ScrollY) && state.ScrollY >= 0 &&
                   state is not PinnedImageState { LiveSource.IsValid: false } &&
                   (state is not PinnedImageState image ||
                    (Enum.IsDefined(image.Orientation) && image.ColorProcessing.Matrix is not { IsFinite: false } &&
                     Guid.TryParseExact(image.ImageId, "N", out _) &&
                     image.Translation is not { IsValid: false } &&
                     image.Ocr is not { IsValid: false } &&
                     (image.TransparentLayer is null || Enum.IsDefined(image.TransparentLayer.Value)) &&
                     // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                     image.TextMarks is not null && image.TextMarks.All(mark => mark is { IsValid: true }) &&
                     image is { ThumbnailStandardWidth: 0, ThumbnailStandardHeight: 0 } or
                         { ThumbnailStandardWidth: > 0, ThumbnailStandardHeight: > 0 } &&
                     double.IsFinite(image.ThumbnailNormalZoom) && image.ThumbnailNormalZoom is >= 0 and <= 8 &&
                     double.IsFinite(image.ThumbnailSavedZoom) && image.ThumbnailSavedZoom >= 0 &&
                     double.IsFinite(image.ThumbnailFullScrollX) && image.ThumbnailFullScrollX >= 0 &&
                     double.IsFinite(image.ThumbnailFullScrollY) && image.ThumbnailFullScrollY >= 0));
        }
    }
}
