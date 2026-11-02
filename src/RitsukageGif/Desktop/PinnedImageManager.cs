using Avalonia.Controls;
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
                var previousGroup = field.ActiveGroup;
                field = value.Normalize();
                if (previousGroup != field.ActiveGroup) ApplyGroupVisibility();
                TrimClosedHistory();
                foreach (var window in _windows) window.RefreshPreferencePresentation();
                foreach (var window in _documentWindows) window.RefreshPreferencePresentation();
            }
        } = new();

        public Func<RecognitionPreferences> RecognitionPreferencesProvider { get; set; } = () => new();
        public Action<FormulaCopyFormat>? FormulaCopyFormatChanged { get; set; }
        public Action<bool>? RecognitionOriginalImageVisibilityChanged { get; set; }
        public Action<FormulaLatexFormat>? FormulaLatexFormatChanged { get; set; }
        public Func<Window, ReadOnlyMemory<byte>, CancellationToken, Task>? ImageTranslationRequested { get; set; }
        public Func<TranslationPreferences> TranslationPreferencesProvider { get; set; } = () => new();

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
        public Action<IReadOnlyList<string>, IReadOnlyList<string>?>? DocumentOutputSaved { get; set; }
        public Action<IReadOnlyList<string>>? OutputNotification { get; set; }

        public Func<byte[], CapturedFrame?, ScreenshotAutoSaveTriggers, CancellationToken, double, Task>? AutomaticSave
        {
            get;
            set;
        }

        public bool HasImages => AllWindows.Any(window => window.Group == Preferences.ActiveGroup);

        public IReadOnlyList<PinnedGroupDefinition> Groups => GroupDefinitions;

        public void Dispose()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            PinLogger.ZLogDebug($"Pinned image manager disposal started. WindowCount={_windows.Count}");
            _disposed = true;
            _groupManager?.Close();
            _groupCreateDialog?.Close();
            _groupSwitcher?.Close();
            _groupSwitcher = null;
            _isolatedVisibleIds.Clear();
            _presentationIds.Clear();
            _historyCancellation.Cancel();
            _historyCancellation.Dispose();
            _saveTimer.Stop();
            foreach (var window in _windows.ToArray()) window.Dispose();
            foreach (var window in _documentWindows.ToArray()) window.Dispose();
            _documentWindows.Clear();
            _windows.Clear();
            foreach (var entry in _closedImages) entry.Dispose();
            _closedImages.Clear();
            _closedDocuments.Clear();
            _selectedImages.Clear();
            _sessionImageReferences.Clear();
            PinLogger.ZLogDebug($"Pinned image manager disposal completed.");
        }

        public event EventHandler? Changed;

        private PinnedImageWindow? Pin(PinnedImageContent content, PixelPoint? position, string group,
            double captureScaling = 1,
            PixelPoint contentOffset = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(group);
            group = ResolveGroupName(group);
            if (Preferences.DuplicateBehavior == PinnedDuplicateBehavior.RestoreLastClosed &&
                RestoreMatchingClosed(content, group)) return null;
            if (Preferences.DuplicateBehavior != PinnedDuplicateBehavior.CreateNew &&
                _windows.FirstOrDefault(candidate =>
                    !candidate.LiveCaptureActive && candidate.Group == group &&
                    candidate.ContentHash == content.ContentHash) is { } existing)
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

                return null;
            }

            var window = PinnedImageWindow.CreatePrepared(content, () => CanRestoreHidden(), Preferences);
            window.SetOriginalCaptureScaling(captureScaling);
            window.SetGroup(group);
            AttachWindow(window);

            try
            {
                if (position is { } location) window.SetCapturePlacement(location, contentOffset);
                ShowNewPin(window);
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
            return window;
        }

        private void OnWindowClosed(object? sender, EventArgs args)
        {
            if (sender is not PinnedImageWindow window || !_windows.Remove(window)) return;
            try
            {
                PinLogger.ZLogInformation($"Pinned image window closed. WindowCount={_windows.Count}");
                window.StateChanged -= WindowStateChanged;
                ForgetPresentation(window);
                _pendingRestore.Remove(window);
                _selectedImages.Remove(window);
                if (!_disposed)
                {
                    if (!window.WasDestroyed) RememberClosed(window);
                    ScheduleSave();
                }

                Changed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                window.ReleaseLiveSnapshot();
            }
        }

        public void ShowAll()
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in AllWindows.Where(window => window.Group == Preferences.ActiveGroup))
                RevealWindow(window);
        }

        public void HideAll()
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in AllWindows.Where(window => window.Group == Preferences.ActiveGroup))
            {
                window.Presentation.SetGroupIntent(false);
                switch (window)
                {
                    case PinnedImageWindow image when _pendingRestore.TryGetValue(image, out var state):
                        state.Visible = false;
                        state.VisibleInGroup = false;
                        break;
                    case PinnedDocumentWindow document when _pendingDocuments.TryGetValue(document, out var saved):
                        saved.Window.Visible = false;
                        saved.Window.VisibleInGroup = false;
                        break;
                }

                window.HideImage();
            }

            ScheduleSave();
        }

        private void AttachWindow(PinnedImageWindow window)
        {
            _windows.Add(window);
            AttachPresentation(window);
            window.PreferencesProvider = () => Preferences;
            window.LiveToneMapProvider = () => CaptureToneMapProvider();
            window.TextRecognitionServiceProvider = () => TextRecognitionService;
            window.RecognitionPreferencesProvider = () => RecognitionPreferencesProvider();
            window.FormulaCopyFormatChanged = format => FormulaCopyFormatChanged?.Invoke(format);
            window.RecognitionOriginalImageVisibilityChanged =
                show => RecognitionOriginalImageVisibilityChanged?.Invoke(show);
            window.FormulaLatexFormatChanged = format => FormulaLatexFormatChanged?.Invoke(format);
            window.PinFormulaRequested = (_, sources) =>
            {
                if (!_disposed) PinFormulas(sources);
            };
            window.TranslationPreferencesProvider = () => TranslationPreferencesProvider();
            window.ImageTranslationRequested = (owner, png, token) => ImageTranslationRequested is { } translate
                ? translate(owner, png, token)
                : ImageTranslationWindow.ShowAsync(owner, png, TranslationPreferencesProvider(),
                    RecognitionPreferencesProvider(), TextRecognitionService, token: token);
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
            window.DocumentOutputSaved = (paths, protectedFiles) => DocumentOutputSaved?.Invoke(paths, protectedFiles);
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
            window.GroupChangeRequested = SetSelectionGroup;
            window.NewGroupRequested = CreateAndMoveToGroupAsync;
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
            if (sender is IPinnedWindow window) RefreshPresentationIdentity(window);
            ScheduleSave();
        }

        public void RestoreInteraction()
        {
            ShowAll();
            foreach (var window in AllWindows.Where(window => window.Group == Preferences.ActiveGroup))
                window.RestoreInteraction();
        }

        public void ShowGroup(string group)
        {
            Dispatcher.UIThread.VerifyAccess();
            SetActiveGroup(group);
            RequestGroupRestore();
            foreach (var window in AllWindows.Where(window => window.Group == group)) RevealWindow(window);
        }

        public void HideGroup(string group)
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in AllWindows.Where(window => window.Group == group))
            {
                window.Presentation.SetGroupIntent(false);
                switch (window)
                {
                    case PinnedImageWindow image when _pendingRestore.TryGetValue(image, out var state):
                        state.Visible = false;
                        state.VisibleInGroup = false;
                        break;
                    case PinnedDocumentWindow document when _pendingDocuments.TryGetValue(document, out var saved):
                        saved.Window.Visible = false;
                        saved.Window.VisibleInGroup = false;
                        break;
                }

                window.HideImage();
            }

            ScheduleSave();
        }

        public void CloseGroup(string group)
        {
            Dispatcher.UIThread.VerifyAccess();
            foreach (var window in AllWindows.Where(window => window.Group == group && !window.IsCloseLocked).ToArray())
                window.CloseImage(false);
        }
    }
}
