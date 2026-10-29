using System.Text.Json;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private const long MaximumSessionBytes = 64L * 1024 * 1024;
        private readonly Dictionary<PinnedImageWindow, PinnedImageState> _pendingRestore = [];
        private readonly HashSet<string> _persistedIds = new(StringComparer.Ordinal);
        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly SemaphoreSlim _sessionGate = new(1);
        private readonly Dictionary<string, PinnedImageState> _unrestoredStates = new(StringComparer.Ordinal);
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
            _sessionDirectory = Path.GetFullPath(directory);
            if (!_saveTimerConfigured)
            {
                _saveTimer.Tick += SaveTimerTick;
                _saveTimerConfigured = true;
            }

            if (restore) return await RestoreSessionAsync(token);
            await _sessionGate.WaitAsync(token);
            try
            {
                var saved = await ReadStatesAsync(token);
                foreach (var entry in saved.States.Where(ValidState))
                {
                    _persistedIds.Add(entry.Id);
                    _unrestoredStates.TryAdd(entry.Id, entry);
                }

                PinLogger.ZLogDebug(
                    $"Pinned image session configuration completed. DeferredCount={_unrestoredStates.Count} RejectedCount={saved.Rejected}");
                return saved.Rejected;
            }
            finally
            {
                _sessionGate.Release();
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
                        if (state is null) rejected++;
                        else states.Add(state);
                    }
                    catch (JsonException)
                    {
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

        public async Task<int> RestoreSessionAsync(CancellationToken token = default)
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
                var knownIds = _windows.Select(window => window.Id)
                    .Concat(_pendingRestore.Values.Select(state => state.Id)).ToHashSet(StringComparer.Ordinal);
                var restoredCount = 0;
                foreach (var state in entries)
                {
                    token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (!ValidState(state))
                    {
                        rejected++;
                        continue;
                    }

                    _persistedIds.Add(state.Id);
                    if (knownIds.Contains(state.Id))
                    {
                        _unrestoredStates.Remove(state.Id);
                        continue;
                    }

                    _unrestoredStates[state.Id] = state;
                    PinnedImageWindow? window = null;
                    try
                    {
                        var imagePath = ImagePath(state.Id);
                        if (new FileInfo(imagePath).Length > 256L * 1024 * 1024)
                        {
                            rejected++;
                            continue;
                        }

                        var png = await File.ReadAllBytesAsync(imagePath, token);
                        token.ThrowIfCancellationRequested();
                        ObjectDisposedException.ThrowIf(_disposed, this);
                        var content = await Task.Run(() => PinnedImageContent.Decode(png, token), token);
                        token.ThrowIfCancellationRequested();
                        window = PinnedImageWindow.CreatePrepared(content, () => CanRestoreHidden(), Preferences);
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

            return rejected;
        }

        public async Task FlushSessionAsync(CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            _saveTimer.Stop();
            if (_sessionDirectory is null || _disposed || !_sessionTouched) return;
            await _sessionGate.WaitAsync(token);
            PinLogger.ZLogDebug($"Pinned image session save started.");
            var createdImages = new List<string>();
            var committed = false;
            try
            {
                if (_disposed) return;
                var images = _windows.Select(window => (
                    State: _pendingRestore.TryGetValue(window, out var pending) ? pending : window.CaptureState(),
                    Png: window.OriginalPng)).ToArray();
                Directory.CreateDirectory(_sessionDirectory);
                foreach (var (state, png) in images)
                {
                    var target = ImagePath(state.Id);
                    if (File.Exists(target)) continue;
                    var temporary = target + ".tmp";
                    try
                    {
                        await File.WriteAllBytesAsync(temporary, png, token);
                        File.Move(temporary, target, false);
                        createdImages.Add(target);
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }

                var states = images.Select(image => image.State).Concat(_unrestoredStates.Values).ToArray();
                var manifest = Path.Combine(_sessionDirectory, "session.json");
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
                }
                finally
                {
                    if (File.Exists(staging)) File.Delete(staging);
                }

                var current = states.Select(state => state.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var expired in _persistedIds.Except(current))
                {
                    var path = ImagePath(expired);
                    if (File.Exists(path)) File.Delete(path);
                }

                _persistedIds.Clear();
                _persistedIds.UnionWith(current);
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

                _sessionGate.Release();
            }
        }

        private void ShowRestoredWindow(PinnedImageWindow window, PinnedImageState state)
        {
            window.ShowActivated = false;
            window.SetGroup(state.Group);
            if ((!state.Visible && CanRestoreHidden()) || !CanRestoreNow())
            {
                _pendingRestore.Add(window, state);
                return;
            }

            window.Show();
            window.RestoreState(state);
        }

        public void ResumeDeferred()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed || !CanRestoreNow()) return;
            foreach (var (window, state) in _pendingRestore.ToArray())
            {
                if (!state.Visible && CanRestoreHidden()) continue;
                _pendingRestore.Remove(window);
                window.Show();
                window.RestoreState(state);
            }
        }

        private void RevealWindow(PinnedImageWindow window)
        {
            if (_pendingRestore.Remove(window, out var state))
            {
                state.Visible = true;
                window.Show();
                window.RestoreState(state);
            }

            window.RevealImage();
        }

        private string ImagePath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException(null, nameof(id));
            return Path.Combine(_sessionDirectory!, id + ".png");
        }

        private static bool ValidState(PinnedImageState? state)
        {
            return state is { Group.Length: <= 80 } && Guid.TryParseExact(state.Id, "N", out _) &&
                   double.IsFinite(state.Zoom) && state.Zoom is > 0 and <= 8 &&
                   double.IsFinite(state.CaptureScaling) && state.CaptureScaling is >= 1 and <= 16 &&
                   double.IsFinite(state.Opacity) && state.Opacity is >= 0.15 and <= 1 &&
                   double.IsFinite(state.ScrollX) && state.ScrollX >= 0 &&
                   double.IsFinite(state.ScrollY) && state.ScrollY >= 0;
        }
    }
}
