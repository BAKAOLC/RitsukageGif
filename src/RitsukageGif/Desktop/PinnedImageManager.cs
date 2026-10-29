using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager : IDisposable
    {
        private static readonly ILogger PinLogger = LogHub.GetLogger("Desktop.Pins");
        private readonly List<PinnedImageWindow> _windows = [];
        private bool _disposed;
        public Func<bool> CanRestoreHidden { get; set; } = () => true;

        public PinnedImagePreferences Preferences
        {
            get;
            set
            {
                field = value.Normalize();
                TrimClosedHistory();
                foreach (var window in _windows) window.RefreshPreferencePresentation();
            }
        } = new();

        public Func<RecognitionPreferences> RecognitionPreferencesProvider { get; set; } = () => new();

        // ReSharper disable once MemberCanBePrivate.Global, AutoPropertyCanBeMadeGetOnly.Global
        public ITextRecognitionService TextRecognitionService { get; set; } = new OcrRecognitionService();
        public AnnotationPreferences AnnotationPreferences { get; set; } = new();
        public Action<AnnotationPreferences>? AnnotationPreferencesChanged { get; set; }
        public Func<ScreenshotSavePreferences> SavePreferencesProvider { get; set; } = () => new();
        public Func<OutputColorMode> OutputColorModeProvider { get; set; } = () => OutputColorMode.Sdr;
        public Func<ScreenshotClipboardPreferences> ClipboardPreferencesProvider { get; set; } = () => new();
        public Func<string?> OutputDirectoryProvider { get; set; } = () => null;
        public Func<AppearanceDialogStyle> DialogStyleProvider { get; set; } = () => AppearanceDialogStyle.Modern;
        public Action<string>? SavedPathChanged { get; set; }
        public Action<IReadOnlyList<string>>? OutputNotification { get; set; }

        public Func<byte[], CapturedFrame?, ScreenshotAutoSaveTriggers, CancellationToken, double, Task>? AutomaticSave
        {
            get;
            set;
        }

        public bool HasImages => _windows.Count != 0;

        public IReadOnlyList<string> Groups =>
        [
            .. _windows.Select(window => window.Group).Where(group => group.Length != 0)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
        ];

        public void Dispose()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            PinLogger.ZLogDebug($"Pinned image manager disposal started. WindowCount={_windows.Count}");
            _disposed = true;
            _saveTimer.Stop();
            foreach (var window in _windows.ToArray()) window.Dispose();
            _windows.Clear();
            _closedImages.Clear();
            _selectedImages.Clear();
            PinLogger.ZLogDebug($"Pinned image manager disposal completed.");
        }

        public event EventHandler? Changed;

        private void Pin(PinnedImageContent content, PixelPoint? position, string group, double captureScaling = 1,
            PixelPoint contentOffset = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(group);
            group = group.Trim();
            if (group.Length > 80) throw new ArgumentOutOfRangeException(nameof(group));
            if (Preferences.DuplicateBehavior == PinnedDuplicateBehavior.RestoreLastClosed &&
                RestoreMatchingClosed(content.ContentHash)) return;
            if (Preferences.DuplicateBehavior != PinnedDuplicateBehavior.CreateNew &&
                _windows.FirstOrDefault(candidate => candidate.ContentHash == content.ContentHash) is { } existing)
            {
                switch (Preferences.DuplicateBehavior)
                {
                    case PinnedDuplicateBehavior.FocusExisting:
                        RevealWindow(existing);
                        existing.Activate();
                        break;
                    case PinnedDuplicateBehavior.RestoreLastClosed:
                        RevealWindow(existing);
                        break;
                    case PinnedDuplicateBehavior.Ignore or PinnedDuplicateBehavior.CreateNew: break;
                    default: throw new InvalidOperationException(nameof(Preferences));
                }

                return;
            }

            var window = PinnedImageWindow.CreatePrepared(content, () => CanRestoreHidden(), Preferences);
            window.SetOriginalCaptureScaling(captureScaling);
            window.SetGroup(group);
            AttachWindow(window);

            try
            {
                if (position is { } location) window.SetCapturePlacement(location, contentOffset);
                window.Show();
                if (position is null) window.RestoreToScreen();
                PinLogger.ZLogInformation(
                    $"Pinned image window opened. WindowCount={_windows.Count} Width={content.Width} Height={content.Height}");
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Pinned image window opening failed.");
                _windows.Remove(window);
                window.Dispose();
                throw;
            }

            ScheduleSave();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void OnWindowClosed(object? sender, EventArgs args)
        {
            if (sender is not PinnedImageWindow window || !_windows.Remove(window)) return;
            PinLogger.ZLogInformation($"Pinned image window closed. WindowCount={_windows.Count}");
            window.StateChanged -= WindowStateChanged;
            _pendingRestore.Remove(window);
            _selectedImages.Remove(window);
            if (!_disposed)
            {
                if (!window.WasDestroyed) RememberClosed(window);
                ScheduleSave();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void ShowAll()
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in _windows) RevealWindow(window);
        }

        public void HideAll()
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in _windows)
            {
                if (_pendingRestore.TryGetValue(window, out var state)) state.Visible = false;
                window.HideImage();
            }

            ScheduleSave();
        }

        private void AttachWindow(PinnedImageWindow window)
        {
            _windows.Add(window);
            window.PreferencesProvider = () => Preferences;
            window.TextRecognitionServiceProvider = () => TextRecognitionService;
            window.RecognitionPreferencesProvider = () => RecognitionPreferencesProvider();
            window.SelectionRequested = ToggleSelection;
            window.CloseRequested = CloseSelection;
            window.MoveSelectionRequested = MoveSelection;
            window.SelectionCountProvider = source => SelectionSnapshot(source).Length;
            window.CanArrangeSelectionProvider = CanArrangeSelection;
            window.ArrangeSelectionRequested = ArrangeSelection;
            window.SaveSelectionRequested = SaveSelectionAsync;
            window.CanSaveSelectionProvider = source => !_savingSelection &&
                                                        SelectionSnapshot(source) is { Length: >= 2 } images &&
                                                        images.All(image => image.CanSaveSelection);
            window.AnnotationPreferencesProvider = () => AnnotationPreferences;
            window.SavePreferencesProvider = () => SavePreferencesProvider();
            window.OutputColorModeProvider = () => OutputColorModeProvider();
            window.ClipboardPreferencesProvider = () => ClipboardPreferencesProvider();
            window.OutputDirectoryProvider = () => OutputDirectoryProvider();
            window.DialogStyleProvider = () => DialogStyleProvider();
            window.SavedPathChanged = path => SavedPathChanged?.Invoke(path);
            window.OutputNotification = message => OutputNotification?.Invoke(message);
            window.AutomaticSave = (png, hdr, trigger, token, captureScaling) =>
                AutomaticSave?.Invoke(png, hdr, trigger, token, captureScaling) ?? Task.CompletedTask;
            window.AnnotationPreferencesChanged = preferences =>
            {
                if (preferences == AnnotationPreferences) return;
                AnnotationPreferencesChanged?.Invoke(preferences);
                AnnotationPreferences = preferences;
            };
            window.GroupsProvider = () => Groups;
            window.GroupVisibilityRequested = (group, visible) =>
            {
                if (visible) ShowGroup(group);
                else HideGroup(group);
            };
            window.Closed += OnWindowClosed;
            window.StateChanged += WindowStateChanged;
        }

        private void WindowStateChanged(object? sender, EventArgs args)
        {
            ScheduleSave();
        }

        public void RestoreInteraction()
        {
            ShowAll();
            foreach (var window in _windows) window.RestoreInteraction();
        }

        public void ShowGroup(string group)
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in _windows.Where(window => window.Group == group)) RevealWindow(window);
        }

        public void HideGroup(string group)
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in _windows.Where(window => window.Group == group))
            {
                if (_pendingRestore.TryGetValue(window, out var state)) state.Visible = false;
                window.HideImage();
            }

            ScheduleSave();
        }

        public void CloseGroup(string group)
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in _windows.Where(window => window.Group == group && !window.IsCloseLocked).ToArray())
                window.CloseImage(false);
        }
    }
}
