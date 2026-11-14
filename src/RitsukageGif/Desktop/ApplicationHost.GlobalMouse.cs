using System.Globalization;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private static readonly List<string> NoGlobalMouseExclusions = [];
        private readonly List<ScreenshotScrollingMaskWindow> _globalMouseMasks = [];
        private GlobalMouseService? _globalMouse;
        private HotkeyRegistrationResult _globalMouseRegistration = new(string.Empty, HotkeyRegistrationState.Disabled);

        private HotkeyRegistrationResult? GetGlobalMouseRegistration(string actionId, string gesture)
        {
            return Settings.GlobalActions.Any(action => action.Enabled && action.Id == actionId) &&
                   Settings.GlobalMouseBindings.Any(binding => binding.Enabled && binding.ActionId == actionId &&
                                                               InputMouseGesture.TryParse(binding.Gesture,
                                                                   out var actual) &&
                                                               InputMouseGesture.TryParse(gesture, out var requested) &&
                                                               actual == requested)
                ? _globalMouseRegistration
                : null;
        }

        private void SetGlobalMouseRegistration(HotkeyRegistrationState state, string? error = null)
        {
            _globalMouseRegistration = new(string.Empty, state, error);
            _settingsWindow?.RefreshHotkeyRegistrations();
        }

        private void RegisterGlobalMouse()
        {
            _globalMouse?.Dispose();
            _globalMouse = null;
            SetGlobalMouseRegistration(HotkeyRegistrationState.Disabled,
                !GlobalMouseService.Supported ? "GlobalMouseUnsupported" :
                IsConfiguring || GlobalInputCapture.Active ? "HotkeyCapturingInput" : null);
            if (!_startServices || !GlobalMouseService.Supported || IsClosing || IsConfiguring ||
                GlobalInputCapture.Active) return;
            SetGlobalMouseRegistration(HotkeyRegistrationState.Pending);
            try
            {
                _globalMouse = new(Settings.GlobalMouseBindings, Settings.GlobalActions,
                    ExecuteGlobalMouseActionAsync, SetStatus,
                    message =>
                    {
                        SetGlobalMouseRegistration(HotkeyRegistrationState.Failed, message);
                        SetInputConfigurationStatus(InputConfigurationStatusSource.GlobalMouse, message);
                    },
                    () =>
                    {
                        SetGlobalMouseRegistration(HotkeyRegistrationState.Registered);
                        ClearInputConfigurationStatus(InputConfigurationStatusSource.GlobalMouse);
                    },
                    GetGlobalMousePreview(), Settings.GlobalMouseIgnoredPrograms);
            }
            catch (Exception error)
            {
                SetGlobalMouseRegistration(HotkeyRegistrationState.Failed, error.Message);
                SetInputConfigurationStatus(InputConfigurationStatusSource.GlobalMouse, error.Message);
            }
        }

        private async Task ExecuteGlobalMouseActionAsync(string id, PixelRect? region)
        {
            CloseGlobalMousePreview();
            var action = Settings.GlobalActions.SingleOrDefault(item => item.Id == id);
            if (action is null || !action.Enabled || IsClosing) return;
            if (!AllowsGlobalMouseInput(action)) return;
            var startsInRegion = region is { IsEmpty: false } && !IsRecording &&
                                 action.Kind is GlobalActionKind.StartRecording or GlobalActionKind.ToggleRecording;
            if (startsInRegion)
            {
                if (IsBusy || IsSelecting || IsConfiguring) return;
            }
            else if (!CanExecuteAction(action))
            {
                return;
            }

            if (region is not { IsEmpty: false } bounds)
            {
                await ExecuteActionAsync(action);
                return;
            }

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (action.Kind)
            {
                case GlobalActionKind.Screenshot:
                case GlobalActionKind.ScreenshotAndCopy:
                case GlobalActionKind.ScreenshotAndPin:
                case GlobalActionKind.ScreenshotAndSave:
                case GlobalActionKind.ScreenshotQuickSave:
                case GlobalActionKind.ScreenshotAndCopyText:
                case GlobalActionKind.ScreenshotAndCopyCodeText:
                case GlobalActionKind.ScreenshotAndTranslate:
                case GlobalActionKind.ScreenshotAndRecognizeText:
                case GlobalActionKind.ScreenshotAndRecognizeTable:
                case GlobalActionKind.ScreenshotAndRecognizeFormula:
                case GlobalActionKind.ScreenshotAndScroll:
                case GlobalActionKind.ScreenshotAndRecord:
                    await ExecuteScreenshotActionAsync(action, bounds);
                    break;
                case GlobalActionKind.SelectRegion:
                case GlobalActionKind.StartRecording:
                case GlobalActionKind.ToggleRecording when !IsRecording:
                    var reply = await ExecuteCommandAsync([
                        "select", bounds.X.ToString(CultureInfo.InvariantCulture),
                        bounds.Y.ToString(CultureInfo.InvariantCulture),
                        bounds.Width.ToString(CultureInfo.InvariantCulture),
                        bounds.Height.ToString(CultureInfo.InvariantCulture),
                    ]);
                    if (!reply.Success)
                    {
                        SetStatus(reply.Message);
                        break;
                    }

                    if (action.Kind is GlobalActionKind.StartRecording or GlobalActionKind.ToggleRecording)
                        await ToggleRecordingAsync();
                    break;
                default:
                    await ExecuteActionAsync(action);
                    break;
            }
        }

        private Action<string?, PixelRect?>? GetGlobalMousePreview()
        {
            return Settings.GlobalMouseShowMask || Settings.GlobalMouseBindings.Any(binding => binding.Enabled &&
                Settings.GlobalActions.Any(action => action.Enabled && action.Id == binding.ActionId &&
                                                     action.Kind == GlobalActionKind.Screenshot))
                ? UpdateGlobalMousePreview
                : null;
        }

        private void UpdateGlobalMousePreview(string? id, PixelRect? region)
        {
            var action = Settings.GlobalActions.SingleOrDefault(item => item.Id == id);
            if (IsClosing || IsBusy || IsSelecting || IsRecording || IsConfiguring ||
                region is not { IsEmpty: false } capture || action is null || !action.Enabled ||
                (!Settings.GlobalMouseShowMask && action.Kind != GlobalActionKind.Screenshot) ||
                !SupportsGlobalMousePreview(action.Kind) || !AllowsGlobalMouseInput(action) ||
                (!CanExecuteAction(action) &&
                 action.Kind is not (GlobalActionKind.StartRecording or GlobalActionKind.ToggleRecording)))
            {
                CloseGlobalMousePreview();
                return;
            }

            var monitors = Screens.All;
            while (_globalMouseMasks.Count > monitors.Count)
            {
                _globalMouseMasks[^1].Close();
                _globalMouseMasks.RemoveAt(_globalMouseMasks.Count - 1);
            }

            for (var index = 0; index < monitors.Count; index++)
            {
                if (index == _globalMouseMasks.Count)
                    _globalMouseMasks.Add(new(Settings.Appearance, (int)Settings.Appearance.SelectionBorderWidth)
                        { Operation = "GlobalMouse" });
                var screen = monitors[index];
                _globalMouseMasks[index].Update(new(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width,
                    screen.Bounds.Height), capture, screen.Scaling, 0);
            }
        }

        private bool AllowsGlobalMouseInput(GlobalActionBinding action)
        {
            var ignoredPrograms = OperatingSystem.IsWindows() && _globalMouse is not null
                ? NoGlobalMouseExclusions
                : Settings.GlobalMouseIgnoredPrograms;
            var program = ignoredPrograms.Count != 0 || action.IgnoredPrograms.Count != 0;
            var fullscreen = Settings.HotkeyPolicy.DisableInFullscreen;
            var context = program || fullscreen
                ? GlobalInputContextReader(fullscreen, program)
                : UnrestrictedInputContext;
            return GlobalInputDisabledReason(action, context, action.Kind == GlobalActionKind.ToggleGlobalShortcuts,
                ignoredPrograms) is null;
        }

        private void CloseGlobalMousePreview()
        {
            foreach (var mask in _globalMouseMasks) mask.Close();
            _globalMouseMasks.Clear();
        }

        private static bool SupportsGlobalMousePreview(GlobalActionKind kind)
        {
            return kind is GlobalActionKind.Screenshot or GlobalActionKind.ScreenshotAndCopy
                or GlobalActionKind.ScreenshotAndPin or GlobalActionKind.ScreenshotAndSave
                or GlobalActionKind.ScreenshotQuickSave or GlobalActionKind.ScreenshotAndCopyText
                or GlobalActionKind.ScreenshotAndCopyCodeText
                or GlobalActionKind.ScreenshotAndTranslate or GlobalActionKind.ScreenshotAndRecognizeText
                or GlobalActionKind.ScreenshotAndRecognizeTable or GlobalActionKind.ScreenshotAndRecognizeFormula
                or GlobalActionKind.ScreenshotAndScroll or GlobalActionKind.ScreenshotAndRecord
                or GlobalActionKind.SelectRegion
                or GlobalActionKind.StartRecording or GlobalActionKind.ToggleRecording;
        }
    }
}
