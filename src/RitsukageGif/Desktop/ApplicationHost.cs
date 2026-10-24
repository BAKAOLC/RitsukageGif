using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly IClassicDesktopStyleApplicationLifetime _desktop;
        private readonly DesktopServices _services = new();
        private readonly PresetController _presets;
        private readonly CommandPipe? _commands;
        private readonly ExternalInputHub _inputs = new();
        private readonly InputChannelServer _inputServer;
        private readonly bool _startServices;
        private HotkeyService? _hotkeys;
        private CaptureRegion? _captureRegion;
        private string? _lastPath;
        private RecordingProgress? _latestProgress;
        private Task<RecordingOutcome>? _operation;
        private bool _recording, _busy, _selecting, _closing, _configuring;
        private bool _manualCaptureCompleted;
        private PixelRect? _region;
        private SelectionCoordinator? _selection;
        private AppSettings _settings = AppSettings.Load();
        private AppSettings? _recordSettings;
        private WindowPreset? _manualPreset;
        private PresetRecordingSettings? _startOverrides;
        private readonly Stopwatch _recordClock = new();
        private int _recordSequence;
        private IReadOnlyList<string> _lastOutputs = [];
        private CancellationTokenSource? _stop, _abort;
        private Task? _shutdown;
        private string _status = UiText.Get("Ready");

        public MainWindow? View { get; private set; }
        public AppSettings Settings => _settings;
        internal AppSettings EffectiveSettings => CanPrepareNextRecording ? _settings : _recordSettings ?? _settings;

        public bool CanPrepareNextRecording => _busy && !_recording && !_closing &&
                                               _manualCaptureCompleted && _presets.ActivePreset is null;

        internal bool CanEditRecordingSettings => !_closing && !_recording && (!_busy || CanPrepareNextRecording);
        public bool IsBusy => _busy;
        public bool IsRecording => _recording;
        public bool IsSelecting => _selecting;
        public bool IsConfiguring => _configuring;
        public bool IsClosing => _closing;
        public PixelRect? Region => _region;
        public string Status => _status;
        public string? LastPath => _lastPath;
        public RecordingProgress? LatestProgress => _latestProgress;
        public IReadOnlyList<string> LastOutputs => _lastOutputs;
        public event Action? Changed;
        private Screens Screens => _services.Screens;

        public ApplicationHost(IClassicDesktopStyleApplicationLifetime desktop) : this(desktop, true)
        {
        }

        internal ApplicationHost(IClassicDesktopStyleApplicationLifetime desktop, bool startServices)
        {
            _startServices = startServices;
            _desktop = desktop;
            _inputServer = new InputChannelServer(_inputs);
            _presets = new PresetController(ApplyPresetActionAsync,
                () => (_recording, _busy, _recordClock.Elapsed.TotalSeconds), SetStatus, () => _lastOutputs,
                () => Volatile.Read(ref _streams), _inputs);
            _services.DisplaysChanged += DisplayChanged;
            if (startServices)
                _commands = new CommandPipe(args => Dispatcher.UIThread.InvokeAsync(() => ExecuteCommandAsync(args)));
            RegisterHotkeys();
        }

        public void SetStatus(string message)
        {
            _status = message;
            Notify();
        }

        private void Notify() => Changed?.Invoke();

        public void ShowMainWindow()
        {
            if (_closing) return;
            var created = View is null;
            if (View is null)
            {
                View = new MainWindow(this);
                _desktop.MainWindow = View;
            }

            WindowActivation.Show(View);
            if (created) StartCleanup();
            if (created) _ = CheckAutomaticUpdateAsync();
            if (created && _lastPath is { } path && !_busy) _ = View.LoadPreviewAsync(path);
        }

        public void CancelRecording()
        {
            _abort?.Cancel();
            SetStatus(UiText.Get("Cancelling"));
        }

        public void SetCommon(int fps, int divisor, OutputFormat format, bool? cursor = null)
        {
            if (!CanEditRecordingSettings) return;
            _settings.Fps = fps;
            _settings.Divisor = divisor;
            _settings.Format = format;
            if (cursor is { } captureCursor) _settings.Cursor = captureCursor;
            _settings.Save();
        }

        public bool BeginConfigure()
        {
            if (!CanEditRecordingSettings || _selecting || _configuring) return false;
            _configuring = true;
            DeactivatePreset();
            Volatile.Write(ref _captureRegion, null);
            _hotkeys?.Dispose();
            _hotkeys = null;
            Notify();
            return true;
        }

        public void EndConfigure()
        {
            _configuring = false;
            if (!_closing) RegisterHotkeys();
            Notify();
        }

        public void UpdateSettings(AppSettings settings)
        {
            if (!CanEditRecordingSettings) return;
            if (settings.CaptureSource != _settings.CaptureSource)
            {
                _manualPreset = null;
                _region = null;
            }

            _settings = settings;
            ResetCleanupCancellation();
            QueueCleanup();
            _settings.Save();
            DeactivatePreset();
            Volatile.Write(ref _captureRegion, null);
            Notify();
        }

        public async Task ShutdownAsync()
        {
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
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
                throw;
            }
            finally
            {
                if (!_closing) _shutdown = null;
            }
        }

        private async Task ShutdownCoreAsync()
        {
            if (!_closing && _animationEditor is { } animationEditor &&
                !await animationEditor.RequestCloseAsync()) return;
            if (!_closing && _editor is { } editor && !await editor.RequestCloseAsync()) return;
            _closing = true;
            _cleanupTimer?.Stop();
            _cleanupCancellation.Cancel();
            await _cleanupTask;
            _cleanupCancellation.Dispose();
            await StopUpdateCheckAsync();
            Notify();
            _commands?.Dispose();
            _hotkeys?.Dispose();
            _hotkeys = null;
            DeactivatePreset();
            CancelSelection();
            _stop?.Cancel();
            _abort?.Cancel();
            if (_selectionOperation is { } selectionOperation)
                try
                {
                    await selectionOperation;
                }
                catch (Exception ex)
                {
                    Trace.TraceInformation("Selection stopped during shutdown: {0}", ex.Message);
                }

            if (_operation is not null) await _operation;
            await _inputServer.DisposeAsync();
            _inputs.Dispose();
            await PlatformShell.StopSoundAsync();
            _services.DisplaysChanged -= DisplayChanged;
            _services.Dispose();
            DesktopCapture.CloseSharedSource();
            View?.CloseForShutdown();
            _desktop.Shutdown(_fatalFailure ? 1 : 0);
        }

        private void RegisterHotkeys()
        {
            _hotkeys?.Dispose();
            _hotkeys = null;
            if (!_startServices || !HotkeyService.Supported) return;
            try
            {
                var bindings = new List<(string Shortcut, Func<Task> Action)>
                    { (_settings.RecordHotkey, ToggleRecordingAsync), (_settings.SelectHotkey, SelectAsync) };
                foreach (var hotkey in _settings.PresetHotkeys)
                    bindings.Add((hotkey.Shortcut, () =>
                    {
                        if (!_busy && !_selecting && !_configuring && !_closing)
                            ActivatePreset(PresetCatalog.Load(_settings.PresetFiles)
                                .Single(p => p.Name == hotkey.Preset));
                        return Task.CompletedTask;
                    }));
                _hotkeys = new HotkeyService(bindings, SetStatus);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }
    }
}
