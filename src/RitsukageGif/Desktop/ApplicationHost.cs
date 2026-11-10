using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private static readonly ILogger HostLogger = LogHub.GetLogger("Desktop.Host");
        private static long _nextOperationId;
        private readonly CaptureHistoryStore _captureHistory = new();
        private readonly CommandPipe? _commands;
        private readonly IClassicDesktopStyleApplicationLifetime _desktop;
        private readonly CancellationTokenSource _hostCancellation = new();
        private readonly InputChannelServer _inputServer;
        private readonly ExternalInputHub _inputs = new();
        private readonly ApplicationHostMode _mode;
        private readonly PinnedImageManager _pinnedImages = new();
        private readonly PresetController _presets;
        private readonly Stopwatch _recordClock = new();
        private readonly DesktopServices _services = new();
        private readonly bool _startServices;
        private CaptureRegion? _captureRegion;
        private HotkeyService? _hotkeys;
        private bool _manualCaptureCompleted;
        private WindowPreset? _manualPreset;
        private Task<RecordingOutcome>? _operation;
        private int _recordSequence;
        private AppSettings? _recordSettings;
        private SelectionCoordinator? _selection;
        private Task? _shutdown;
        private PresetRecordingSettings? _startOverrides;
        private string _status = UiText.Get("Ready");
        private string? _statusKey = "Ready";
        private CancellationTokenSource? _stop, _abort;

        public ApplicationHost(IClassicDesktopStyleApplicationLifetime desktop) : this(desktop, true)
        {
        }

        internal ApplicationHost(IClassicDesktopStyleApplicationLifetime desktop, TaskHostOptions task)
            : this(desktop, true, ApplicationHostMode.Task, task)
        {
        }

        private ApplicationHost(IClassicDesktopStyleApplicationLifetime desktop, bool startServices,
            ApplicationHostMode mode = ApplicationHostMode.Desktop, TaskHostOptions? task = null)
        {
            _mode = mode;
            TaskKeepAlive = task?.KeepAlive == true;
            _startServices = startServices && mode == ApplicationHostMode.Desktop;
            HostLogger.ZLogInformation(
                $"Application host initialization started. Mode={mode.ToString()} DesktopServicesEnabled={_startServices}");
            if (IsTaskHost) Settings = Settings.Snapshot();
            InitializeNetworkPreferences();
            _globalShortcutsSuspended = !Settings.HotkeyPolicy.ShortcutsEnabled;
            _persistedStartWithWindows = Settings.System.StartWithWindows;
            _persistedStartupHidden = Settings.System.StartupHidden;
            AppTheme.Apply(Settings.Appearance);
            _pinnedImages.Preferences = Settings.PinnedImages;
            _pinnedImages.TrayVisibleProvider = () => _tray is { IsVisible: true };
            _pinnedImages.OpenConfigurationRequested = OpenSettingsAsync;
            _pinnedImages.PreferencesChanged = preferences =>
            {
                Settings.PinnedImages = preferences;
                QueueSettingsSave(Settings);
            };
            _pinnedImages.AnnotationPreferences = Settings.Annotation;
            _pinnedImages.RecognitionPreferencesProvider = () => Settings.Recognition;
            _pinnedImages.RecognitionDialogPreferencesChanged = PersistRecognitionDialog;
            _pinnedImages.DirectRecognitionCopyRequested = DisableRecognitionConfirmation;
            _pinnedImages.FormulaCopyFormatChanged = PersistFormulaCopyFormat;
            _pinnedImages.RecognitionOriginalImageVisibilityChanged = PersistRecognitionOriginalImage;
            _pinnedImages.FormulaLatexFormatChanged = PersistFormulaLatexFormat;
            _pinnedImages.TranslationPreferencesProvider = () => Settings.Translation;
            _pinnedImages.ImageTranslationRequested = ShowImageTranslationAsync;
            _pinnedImages.AnnotationPreferencesChanged = PersistAnnotationPreferences;
            _pinnedImages.SavePreferencesProvider = () => IsTaskHost
                ? Settings.ScreenshotSave with
                {
                    AfterSave = ScreenshotAfterSaveAction.None,
                    Notify = false,
                    AutoSaveEnabled = false,
                    AutoSaveTriggers = ScreenshotAutoSaveTriggers.None,
                }
                : Settings.ScreenshotSave;
            _pinnedImages.OutputColorModeProvider = () => Settings.CaptureColorMode;
            _pinnedImages.CaptureToneMapProvider = () => Settings.CaptureToneMap;
            _pinnedImages.ClipboardPreferencesProvider = () => Settings.ScreenshotClipboard;
            _pinnedImages.OutputDirectoryProvider = () => Settings.OutputDirectory;
            _pinnedImages.DialogStyleProvider = () => Settings.Appearance.SaveDialogStyle;
            _pinnedImages.SavedPathChanged = PersistScreenshotExtension;
            _pinnedImages.DocumentOutputSaved = QueueCleanup;
            _pinnedImages.OutputNotification = IsTaskHost ? null : ShowOutputNotification;
            _pinnedImages.AutomaticSave = IsTaskHost ? null : AutoSaveScreenshotAsync;
            _desktop = desktop;
            NativeDisplayCatalog.Update(Screens);
            _pinnedImages.CanRestoreHidden = () => CanHideToTray || CanRestoreTaskPins;
            _pinnedImages.CanRestoreNow = () => !IsSelecting && !IsClosing;
            _pinnedImages.SessionError = error =>
            {
                if (!IsClosing) SetStatus(error.Message);
            };
            _inputServer = new(_inputs);
            var textRecognition = new OcrRecognitionService();
            var barcodeRecognition = new BarcodeRecognitionService();
            _presets = new(ApplyPresetActionAsync,
                () => (IsRecording, IsBusy, _recordClock.Elapsed.TotalSeconds), SetStatus, () => LastOutputs,
                () => Volatile.Read(ref _streams), _inputs, this, this,
                textRecognition, barcodeRecognition,
                new PrivacyDetectionService([
                    new OcrPrivacyCandidateProvider(textRecognition),
                    new BarcodePrivacyCandidateProvider(barcodeRecognition),
                ]), ConfigureAutomationBindings);
            _services.DisplaysChanged += DisplayChanged;
            var shortcutErrors =
                SelectionCommands.Configure(Settings.SelectionShortcuts, Settings.MixingKeypadMainKeyboard);
            if (shortcutErrors.Count > 0)
            {
                SelectionCommands.Configure(null, Settings.MixingKeypadMainKeyboard);
                _statusKey = null;
                _status = string.Join(Environment.NewLine, shortcutErrors);
                _inputStatusSource = InputConfigurationStatusSource.SelectionShortcuts;
            }

            var mouseErrors = MouseCommands.Configure(Settings.MouseShortcuts);
            if (mouseErrors.Count > 0)
            {
                MouseCommands.Configure(null);
                _statusKey = null;
                _status = string.Join(Environment.NewLine, mouseErrors);
                _inputStatusSource = InputConfigurationStatusSource.MouseShortcuts;
            }

            if (startServices)
                _commands = new(
                    (args, token) => Dispatcher.UIThread.InvokeAsync(() => ExecuteCommandAsync(args, token)),
                    task is null ? null : CommandPipe.SessionName(task.SessionId),
                    IsTaskHost ? TaskCommandServerStopped : null);
            InitializeGlobalInputPolicy();
            RegisterHotkeys();
            RegisterGlobalMouse();
            if (_startServices) GlobalInputCapture.Changed += GlobalInputCaptureChanged;
            InitializeSessionShortcuts();
            InitializeTray();
            InitializeSystemFeatures();
            if (_startServices)
            {
                _pinStartup = InitializePinnedSessionAsync();
                ConfigureCaptureHistoryPersistence();
            }
            else if (IsTaskHost)
            {
                ConfigureCaptureHistoryBudget();
                ConfigureTaskSettingsAutomation(Settings);
                InitializeTaskStartupGuard(task!);
            }
        }

        private MainWindow? View { get; set; }
        public AppSettings Settings { get; private set; } = AppSettings.Load();
        internal bool IsTaskHost => _mode == ApplicationHostMode.Task;

        internal bool CompletedExpectedTaskShutdown =>
            IsTaskHost && !_fatalFailure && _shutdown?.IsCompletedSuccessfully == true;

        private int TaskExitCode { get; set; }

        internal AppSettings EffectiveSettings => CanPrepareNextRecording ? Settings : _recordSettings ?? Settings;

        private bool CanPrepareNextRecording => IsBusy && !IsRecording && !IsClosing &&
                                                _manualCaptureCompleted && _presets.ActivePreset is null;

        internal bool CanEditRecordingSettings => !IsClosing && !IsRecording && (!IsBusy || CanPrepareNextRecording);

        internal bool CanToggleRecording => !IsClosing && !_screenshotRecordingActive &&
                                            !_screenshotRecordingPreparing && (IsRecording ||
                                                                               (!IsSelecting && !IsConfiguring &&
                                                                                   !IsBusy &&
                                                                                   Region is not null));

        public bool IsBusy { get; private set; }

        public bool IsRecording { get; private set; }

        public bool IsSelecting { get; private set; }

        public bool IsConfiguring { get; private set; }

        public bool IsClosing { get; private set; }

        public PixelRect? Region { get; private set; }

        public string Status => _statusKey is { } key ? UiText.Get(key) : _status;

        public string? LastPath
        {
            get;
            private set
            {
                field = value;
                LastScreenshotOutput = null;
            }
        }

        internal ScreenshotSavedOutput? LastScreenshotOutput { get; private set; }
        internal string? LatestOutputPath => LastScreenshotOutput?.Paths[0] ?? LastPath;

        public RecordingProgress? LatestProgress { get; private set; }

        public IReadOnlyList<string> LastOutputs { get; private set; } = [];

        private Screens Screens => _services.Screens;
        public event Action? Changed;

        public void SetStatus(string message)
        {
            _inputStatusSource = InputConfigurationStatusSource.None;
            _statusKey = null;
            _status = message;
            Notify();
        }

        private void SetStatusKey(string key)
        {
            _inputStatusSource = InputConfigurationStatusSource.None;
            _statusKey = key;
            Notify();
        }

        private void Notify()
        {
            _pinnedImages.ResumeDeferred();
            RefreshDesktopToolbar();
            Changed?.Invoke();
        }

        public void ShowMainWindow()
        {
            if (IsClosing) return;
            _desktopToolbarSuppressedAtStartup = false;
            RefreshDesktopToolbar();
            var created = View is null;
            if (View is null)
            {
                View = new(this);
                _desktop.MainWindow = View;
            }

            View.RestoreFromTray();
            WindowActivation.Show(View);
            if (created) StartCleanup();
            if (created) _ = CheckAutomaticUpdateAsync();
            if (created && LastPath is { } path && !IsBusy) _ = View.LoadPreviewAsync(path);
            RefreshTray();
        }

        public void CancelRecording()
        {
            _abort?.Cancel();
            SetStatusKey("Cancelling");
        }

        public void SetCommon(int fps, int divisor, OutputFormat format, bool? cursor = null)
        {
            if (!CanEditRecordingSettings) return;
            var settings = Settings with
            {
                Fps = fps, Divisor = divisor, Format = format, Cursor = cursor ?? Settings.Cursor,
            };
            QueueSettingsSave(settings);
            Settings = settings;
            Notify();
        }

        public bool BeginConfigure()
        {
            if (!CanEditRecordingSettings || IsSelecting || IsConfiguring) return false;
            IsConfiguring = true;
            _hotkeys?.Dispose();
            _hotkeys = null;
            ResetHotkeyRegistrations();
            _globalMouse?.Dispose();
            _globalMouse = null;
            Notify();
            return true;
        }

        public void EndConfigure()
        {
            IsConfiguring = false;
            if (!IsClosing) RegisterHotkeys();
            if (!IsClosing) RegisterGlobalMouse();
            Notify();
        }

        public void UpdateSettings(AppSettings settings)
        {
            if (IsClosing) return;
            if (IsTaskHost)
            {
                ApplyTaskSettingsAutomation(SettingsAutomationJson.Validate(settings, CancellationToken.None), null,
                    false);
                return;
            }

            if (settings.Network != Settings.Network)
                throw new InvalidOperationException(UiText.Get("NetworkProxyNeedsVerification"));
            if (Settings.ContentEquals(settings)) return;
            settings = settings.Snapshot();
            settings.HotkeyPolicy = settings.HotkeyPolicy.Normalize();
            settings.System = settings.System.Normalize();
            settings.Network = settings.Network.Normalize();
            settings.Recognition = settings.Recognition.Normalize();
            var previous = Settings;
            var shortcutErrors =
                SelectionCommands.Validate(settings.SelectionShortcuts, settings.MixingKeypadMainKeyboard);
            if (shortcutErrors.Count > 0 &&
                (previous.MixingKeypadMainKeyboard != settings.MixingKeypadMainKeyboard ||
                 !SameShortcuts(previous.SelectionShortcuts, settings.SelectionShortcuts)))
                throw new ArgumentException(string.Join(Environment.NewLine, shortcutErrors), nameof(settings));
            var actionErrors = GlobalActions.Validate(settings.GlobalActions, ReservedActionShortcuts(settings),
                settings.GlobalMouseBindings);
            var actionsChanged = previous.HotkeyPolicy.ShortcutsEnabled != settings.HotkeyPolicy.ShortcutsEnabled ||
                                 previous.HotkeyPolicy.DisableInFullscreen !=
                                 settings.HotkeyPolicy.DisableInFullscreen ||
                                 !previous.HotkeyPolicy.IgnoredPrograms.SequenceEqual(
                                     settings.HotkeyPolicy.IgnoredPrograms, ProgramExclusionRules.Comparer) ||
                                 !SameActions(previous.GlobalActions, settings.GlobalActions) ||
                                 !previous.PresetHotkeys.SequenceEqual(settings.PresetHotkeys) ||
                                 !previous.GlobalMouseBindings.SequenceEqual(settings.GlobalMouseBindings);
            if (actionErrors.Count > 0 && actionsChanged)
                throw new ArgumentException(string.Join(Environment.NewLine, actionErrors), nameof(settings));
            var mouseErrors = MouseCommands.Validate(settings.MouseShortcuts);
            if (mouseErrors.Count > 0 && !SameShortcuts(previous.MouseShortcuts, settings.MouseShortcuts))
                throw new ArgumentException(string.Join(Environment.NewLine, mouseErrors), nameof(settings));
            var globalMouseErrors = GlobalMouseService.Validate(settings.GlobalMouseBindings, settings.GlobalActions);
            if (globalMouseErrors.Count > 0 && actionsChanged)
                throw new ArgumentException(string.Join(Environment.NewLine, globalMouseErrors), nameof(settings));
            foreach (var action in settings.GlobalActions)
                action.IgnoredPrograms = ProgramExclusionRules.Normalize(action.IgnoredPrograms);
            QueueSettingsSave(settings);
            if (CanEditRecordingSettings && settings.CaptureSource != Settings.CaptureSource &&
                _presets.ActivePreset is null)
            {
                _manualPreset = null;
                Region = null;
                Volatile.Write(ref _captureRegion, null);
            }

            Settings = settings;
            if (previous.CapturePreferences.PersistHistory != settings.CapturePreferences.PersistHistory ||
                previous.CapturePreferences.RegionHistoryCapacity !=
                settings.CapturePreferences.RegionHistoryCapacity ||
                previous.CapturePreferences.ScreenshotHistoryCapacity !=
                settings.CapturePreferences.ScreenshotHistoryCapacity ||
                previous.CapturePreferences.ScreenshotHistoryMaximumMiB !=
                settings.CapturePreferences.ScreenshotHistoryMaximumMiB)
                ConfigureCaptureHistoryPersistence();
            if (previous.GlobalMouseShowMask != settings.GlobalMouseShowMask ||
                previous.Appearance != settings.Appearance)
                CloseGlobalMousePreview();
            _globalShortcutsSuspended = !settings.HotkeyPolicy.ShortcutsEnabled;
            if (previous.System.DesktopToolbar.Mode != settings.System.DesktopToolbar.Mode)
                _desktopToolbarSuppressedAtStartup = false;
            if (previous.MixingKeypadMainKeyboard != settings.MixingKeypadMainKeyboard ||
                !SameShortcuts(previous.SelectionShortcuts, settings.SelectionShortcuts))
                SelectionCommands.Configure(settings.SelectionShortcuts, settings.MixingKeypadMainKeyboard);
            var appearanceChanged = previous.Appearance != settings.Appearance;
            if (appearanceChanged) AppTheme.Apply(settings.Appearance);
            if (!AppSettings.Same(previous.PinnedImages, settings.PinnedImages))
                _pinnedImages.Preferences = settings.PinnedImages;
            else if (appearanceChanged)
                _pinnedImages.RefreshPresentation();
            _pinnedImages.AnnotationPreferences = settings.Annotation;
            if (!SameShortcuts(previous.MouseShortcuts, settings.MouseShortcuts))
                MouseCommands.Configure(settings.MouseShortcuts);
            ClearValidatedInputConfigurationStatus(shortcutErrors.Count == 0, mouseErrors.Count == 0,
                actionErrors.Count == 0);
            if (previous.Language != settings.Language) LiveText.ApplyLanguage(settings.Language);
            if (previous.CleanupEnabled != settings.CleanupEnabled ||
                previous.CleanupMaxAgeDays != settings.CleanupMaxAgeDays ||
                previous.CleanupMaxFiles != settings.CleanupMaxFiles ||
                previous.CleanupMaxSizeMiB != settings.CleanupMaxSizeMiB)
            {
                ResetCleanupCancellation();
                QueueCleanup();
            }

            if (!SameActions(previous.GlobalActions, settings.GlobalActions) ||
                !previous.PresetHotkeys.SequenceEqual(settings.PresetHotkeys) ||
                !previous.PresetFiles.SequenceEqual(settings.PresetFiles, StringComparer.Ordinal))
                RebuildActionMenus();
            switch (IsConfiguring)
            {
                case false when actionsChanged:
                    RegisterHotkeys();
                    RegisterGlobalMouse();
                    break;
                case false when previous.GlobalMouseShowMask != settings.GlobalMouseShowMask:
                    RegisterGlobalMouse();
                    break;
            }

            View?.RefreshSettingsPresentation(previous, settings);
            Notify();
        }

        public async Task ShutdownAsync()
        {
            HostLogger.ZLogInformation(
                $"Application shutdown requested. Closing={IsClosing} Recording={IsRecording} Selecting={IsSelecting}");
            if (_shutdown is { } pending)
            {
                await pending;
                return;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdown = completion.Task;
            try
            {
                await ShutdownCoreAsync();
                HostLogger.ZLogInformation(
                    $"Application shutdown completed. Closing={IsClosing} FatalFailure={_fatalFailure}");
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
                HostLogger.ZLogError(error, $"Application shutdown failed.");
                throw;
            }
            finally
            {
                if (!IsClosing) _shutdown = null;
            }
        }

        private async Task ShutdownCoreAsync()
        {
            CompleteTaskStartupGuard();
            _settingsWindow?.Close();
            if (!IsClosing)
            {
                if (_animationEditor is { } animationEditor && !await animationEditor.RequestCloseAsync()) return;
                if (_editor is { } editor && !await editor.RequestCloseAsync()) return;
            }

            IsClosing = true;
            var hostToken = _hostCancellation.Token;
            await RunShutdownStageAsync("host cancellation", _hostCancellation.CancelAsync, CancellationToken.None);
            foreach (var imageEditor in _imageEditors.ToArray()) imageEditor.Close();
            await RunShutdownStageAsync("image editor cleanup", () => _imageEditorCleanup,
                CancellationToken.None);
            _imageEditorCleanup = Task.CompletedTask;
            var selectionOperation = _selectionOperation;
            var screenshotOperation = _screenshotOperation;
            var recordingOperation = _operation;
            var cleanupToken = _cleanupCancellation.Token;
            var selectionToken = _selectionCancellation?.Token ?? CancellationToken.None;
            var screenshotToken = _screenshotCancellation?.Token ?? CancellationToken.None;
            var stopToken = _stop?.Token ?? CancellationToken.None;
            var abortToken = _abort?.Token ?? CancellationToken.None;
            if (_startServices) GlobalInputCapture.Changed -= GlobalInputCaptureChanged;
            await RunShutdownStageAsync("foreground hotkey monitor", StopGlobalInputPolicyAsync,
                CancellationToken.None);
            RunShutdownStage("session shortcuts", StopSessionShortcuts);
            RunShutdownStage("system features", StopSystemFeatures);
            RunShutdownStage("output notification", CloseOutputNotification);
            await RunShutdownStageAsync("pinned image startup cancellation", _pinStartupCancellation.CancelAsync,
                CancellationToken.None);
            RunShutdownStage("cleanup timer", () => _cleanupTimer?.Stop());
            await RunShutdownStageAsync("cleanup cancellation", _cleanupCancellation.CancelAsync,
                CancellationToken.None);
            await RunShutdownStageAsync("recording cleanup", () => _cleanupTask, cleanupToken);
            RunShutdownStage("cleanup cancellation source", _cleanupCancellation.Dispose);
            await RunShutdownStageAsync("update check", StopUpdateCheckAsync, _updateCancellation.Token);
            RunShutdownStage("closing notification", Notify);
            var nativeTray = PortableTrayState.Current;
            RunShutdownStage("tray", DisposeTray);
            if (nativeTray is not null)
                await RunShutdownStageAsync("native tray helper", () => nativeTray.Completion, CancellationToken.None);
            RunShutdownStage("command server", () => _commands?.Dispose());
            RunShutdownStage("global mouse", () => _globalMouse?.Dispose());
            _globalMouse = null;
            RunShutdownStage("global hotkeys", () => _hotkeys?.Dispose());
            _hotkeys = null;
            RunShutdownStage("preset", DeactivatePreset);
            RunShutdownStage("selection cancellation", CancelSelection);
            RunShutdownStage("screenshot cancellation", CancelScreenshot);
            if (_stop is { } stop)
                await RunShutdownStageAsync("recording stop", stop.CancelAsync, CancellationToken.None);
            if (_abort is { } abort)
                await RunShutdownStageAsync("recording abort", abort.CancelAsync, CancellationToken.None);
            if (selectionOperation is not null)
                await RunShutdownStageAsync("selection", () => selectionOperation, selectionToken);
            if (screenshotOperation is not null)
                await RunShutdownStageAsync("screenshot", () => screenshotOperation, screenshotToken);
            await RunShutdownStageAsync("inline action scripts", StopActionScriptsAsync, CancellationToken.None);
            await RunShutdownStageAsync("translation windows", () => Task.WhenAll(_translationTasks.ToArray()),
                hostToken);
            await RunShutdownStageAsync("record clearing", () => _recordClearingTask, CancellationToken.None);
            await RunShutdownStageAsync("pinned image session", StopPinnedSessionAsync, CancellationToken.None);
            await RunShutdownStageAsync("capture history", StopCaptureHistoryAsync, CancellationToken.None);
            await RunShutdownStageAsync("structured recognition worker",
                () => StructuredRecognitionProcess.Shared.DisposeAsync().AsTask(), CancellationToken.None);
            if (recordingOperation is not null)
                await RunShutdownStageAsync("recording", () => recordingOperation,
                    abortToken.IsCancellationRequested ? abortToken : stopToken);
            await RunShutdownStageAsync("screenshot recorder", CloseScreenshotRecorderForShutdownAsync,
                CancellationToken.None);
            if (_settingsPersistence is { } persistence)
                await RunShutdownStageAsync("final settings persistence", () => persistence.DisposeAsync().AsTask(),
                    CancellationToken.None);
            await RunShutdownStageAsync("input channels", () => _inputServer.DisposeAsync().AsTask(),
                CancellationToken.None);
            RunShutdownStage("external inputs", _inputs.Dispose);
            await RunShutdownStageAsync("sound", PlatformShell.StopSoundAsync, CancellationToken.None);
            _services.DisplaysChanged -= DisplayChanged;
            RunShutdownStage("desktop services", _services.Dispose);
            RunShutdownStage("shared capture source", DesktopCapture.CloseSharedSource);
            RunShutdownStage("video recorder window", CloseVideoRecorderForShutdown);
            RunShutdownStage("main window", () => View?.CloseForShutdown());
            RunShutdownStage("host cancellation source", _hostCancellation.Dispose);
            RunShutdownStage("desktop lifetime",
                () => _desktop.Shutdown(_fatalFailure ? 1 : IsTaskHost ? TaskExitCode : 0));
        }

        private List<(string Shortcut, Func<Task> Action)> BuildGlobalHotkeyBindings()
        {
            var context = _registeredInputContext ?? ReadGlobalInputContext();
            var bindings = new List<(string Shortcut, Func<Task> Action)>();
            // Build bindings directly without allocating a LINQ iterator and closure for the list projection.
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var hotkey in Settings.PresetHotkeys)
            {
                if (GlobalInputDisabledReason(null, context) is { } reason)
                {
                    SetHotkeyDisabled(hotkey.Shortcut, reason);
                    continue;
                }

                bindings.Add((hotkey.Shortcut, () =>
                {
                    if (!IsBusy && !IsSelecting && !IsConfiguring && !IsClosing)
                        ActivatePreset(PresetCatalog.Load(Settings.PresetFiles)
                            .Single(p => p.Name == hotkey.Preset));
                    return Task.CompletedTask;
                }));
            }

            var actionErrors = GlobalActions.Validate(Settings.GlobalActions, ReservedActionShortcuts(),
                Settings.GlobalMouseBindings);
            if (actionErrors.Count != 0)
                SetInputConfigurationStatus(InputConfigurationStatusSource.GlobalActions,
                    string.Join(Environment.NewLine, actionErrors));
            else
                // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                foreach (var action in Settings.GlobalActions)
                    if (action.Enabled && !string.IsNullOrWhiteSpace(action.Shortcut))
                    {
                        var reason = GlobalInputDisabledReason(action, context,
                            action.Kind == GlobalActionKind.ToggleGlobalShortcuts);
                        if (reason is null)
                            bindings.Add((action.Shortcut, () => ExecutePinnedKeyboardActionAsync(action)));
                        else SetHotkeyDisabled(action.Shortcut, reason);
                    }

            return bindings;
        }

        private void RegisterHotkeys()
        {
            _hotkeys?.Dispose();
            _hotkeys = null;
            ResetHotkeyRegistrations();
            ConfigureGlobalInputPolicyRecovery();
            if (!_startServices || !HotkeyService.Supported || IsClosing || IsConfiguring ||
                GlobalInputCapture.Active) return;
            try
            {
                _registeredInputContext = ReadGlobalInputContext();
                var bindings = BuildGlobalHotkeyBindings();
                for (var index = 0; index < bindings.Count; index++)
                    bindings[index] = (bindings[index].Shortcut,
                        GuardGlobalInput(bindings[index].Action, bindings[index].Shortcut));
                AddSessionShortcuts(bindings);
                foreach (var binding in bindings)
                    _hotkeyRegistrations[HotkeyService.Parse(binding.Shortcut)] = new(binding.Shortcut,
                        HotkeyRegistrationState.Pending);
                _hotkeys = new(bindings, SetStatus,
                    message => SetInputConfigurationStatus(InputConfigurationStatusSource.GlobalHotkeys, message),
                    () => ClearInputConfigurationStatus(InputConfigurationStatusSource.GlobalHotkeys),
                    SetHotkeyRegistration, DescribeGlobalShortcut);
            }
            catch (Exception ex)
            {
                FailPendingHotkeyRegistrations(ex.Message);
                SetInputConfigurationStatus(InputConfigurationStatusSource.GlobalHotkeys, ex.Message);
            }
        }
    }
}
