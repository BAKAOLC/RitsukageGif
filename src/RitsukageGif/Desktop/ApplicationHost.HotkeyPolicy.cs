using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private static readonly ForegroundHotkeyContext UnrestrictedInputContext = new(null, false);
        private IDisposable? _foregroundHotkeys;
        private List<Task>? _foregroundPolicyStops;
        private DispatcherTimer? _fullscreenPolicyRecovery;
        private bool _globalShortcutsSuspended;
        private ForegroundHotkeyContext? _registeredInputContext;

        // ReSharper disable once MemberCanBePrivate.Global
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        internal Func<bool, bool, ForegroundHotkeyContext> GlobalInputContextReader { private get; init; } =
            ForegroundHotkeyContext.Read;

        private ForegroundHotkeyContext ReadGlobalInputContext()
        {
            var program = Settings.HotkeyPolicy.IgnoredPrograms.Count != 0 ||
                          Settings.GlobalActions.Any(action => action.Enabled && action.IgnoredPrograms.Count != 0);
            var fullscreen = Settings.HotkeyPolicy.DisableInFullscreen;
            return program || fullscreen ? GlobalInputContextReader(fullscreen, program) : UnrestrictedInputContext;
        }

        private void InitializeGlobalInputPolicy()
        {
            if (!_startServices) return;
            ConfigureGlobalInputPolicyRecovery();
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                _foregroundHotkeys = new ForegroundHotkeyMonitor(RefreshGlobalInputPolicy);
            }
            catch (Exception error)
            {
                HostLogger.ZLogError(error, $"Foreground hotkey monitoring could not be started.");
                SetInputConfigurationStatus(InputConfigurationStatusSource.ForegroundHotkeys, error.Message);
            }
        }

        private void SetForegroundPolicyStatus(ForegroundHotkeyContext context)
        {
            var needsProgram = Settings.HotkeyPolicy.IgnoredPrograms.Count != 0 ||
                               Settings.GlobalMouseIgnoredPrograms.Count != 0 ||
                               Settings.GlobalActions.Any(action =>
                                   action.Enabled && action.IgnoredPrograms.Count != 0);
            if ((needsProgram && !context.ProgramAvailable) ||
                (Settings.HotkeyPolicy.DisableInFullscreen && !context.FullscreenAvailable))
                SetInputConfigurationStatus(InputConfigurationStatusSource.ForegroundHotkeys,
                    UiText.Get(context.UnavailableReason ?? "HotkeyPolicyObserverUnavailable"));
            else
                ClearInputConfigurationStatus(InputConfigurationStatusSource.ForegroundHotkeys);
        }

        private void ToggleGlobalShortcuts()
        {
            _globalShortcutsSuspended = !_globalShortcutsSuspended;
            Settings.HotkeyPolicy = Settings.HotkeyPolicy with { ShortcutsEnabled = !_globalShortcutsSuspended };
            QueueSettingsSave(Settings);
            RegisterHotkeys();
            RefreshActionMenus();
            Notify();
        }

        private void ConfigureGlobalInputPolicyRecovery()
        {
            if (!OperatingSystem.IsWindows())
            {
                ConfigurePortableForegroundPolicy();
                StopFullscreenPolicyRecovery();
                return;
            }

            if (!_startServices || !OperatingSystem.IsWindows() || IsClosing ||
                !Settings.HotkeyPolicy.DisableInFullscreen)
            {
                StopFullscreenPolicyRecovery();
                return;
            }

            if (_fullscreenPolicyRecovery is not null) return;
            _fullscreenPolicyRecovery = new() { Interval = TimeSpan.FromSeconds(8) };
            _fullscreenPolicyRecovery.Tick += FullscreenPolicyRecoveryTick;
            _fullscreenPolicyRecovery.Start();
        }

        private void FullscreenPolicyRecoveryTick(object? sender, EventArgs args)
        {
            RefreshGlobalInputPolicy();
        }

        private void StopFullscreenPolicyRecovery()
        {
            if (_fullscreenPolicyRecovery is not { } timer) return;
            _fullscreenPolicyRecovery = null;
            timer.Stop();
            timer.Tick -= FullscreenPolicyRecoveryTick;
        }

        private void ConfigurePortableForegroundPolicy()
        {
            var needed = Settings.HotkeyPolicy.DisableInFullscreen ||
                         Settings.HotkeyPolicy.IgnoredPrograms.Count != 0 ||
                         Settings.GlobalMouseIgnoredPrograms.Count != 0 ||
                         Settings.GlobalActions.Any(action => action.Enabled && action.IgnoredPrograms.Count != 0);
            if (!_startServices || IsClosing || !needed)
            {
                StopForegroundPolicyObserver();
                ClearInputConfigurationStatus(InputConfigurationStatusSource.ForegroundHotkeys);
                return;
            }

            if (!PortableForegroundPolicy.Supported)
            {
                StopForegroundPolicyObserver();
                SetForegroundPolicyStatus(PortableForegroundPolicy.Read());
                return;
            }

            if (_foregroundHotkeys is null)
                try
                {
                    _foregroundHotkeys =
                        new PortableForegroundPolicy(RefreshGlobalInputPolicy, SetForegroundPolicyStatus);
                }
                catch (Exception error)
                {
                    HostLogger.ZLogError(error, $"Foreground hotkey monitoring could not be started.");
                    SetInputConfigurationStatus(InputConfigurationStatusSource.ForegroundHotkeys, error.Message);
                    return;
                }

            SetForegroundPolicyStatus(PortableForegroundPolicy.Read());
        }

        private void StopForegroundPolicyObserver()
        {
            var observer = _foregroundHotkeys;
            _foregroundHotkeys = null;
            observer?.Dispose();
            if (observer is not PortableForegroundPolicy portable) return;
            _foregroundPolicyStops?.RemoveAll(static task => task.IsCompletedSuccessfully);
            if (!portable.Completion.IsCompletedSuccessfully)
                (_foregroundPolicyStops ??= []).Add(portable.Completion);
        }

        private Task StopGlobalInputPolicyAsync()
        {
            StopFullscreenPolicyRecovery();
            StopForegroundPolicyObserver();
            if (_foregroundPolicyStops is not { Count: > 0 } pending) return Task.CompletedTask;
            var completion = Task.WhenAll(pending);
            pending.Clear();
            return completion;
        }

        private void RefreshGlobalInputPolicy()
        {
            if (IsClosing || IsConfiguring) return;
            var context = ReadGlobalInputContext();
            if (_registeredInputContext is { } previous && previous.Fullscreen == context.Fullscreen &&
                previous.ProgramAvailable == context.ProgramAvailable &&
                previous.FullscreenAvailable == context.FullscreenAvailable &&
                previous.UnavailableReason == context.UnavailableReason &&
                ProgramExclusionRules.Comparer.Equals(previous.Program, context.Program)) return;
            RegisterHotkeys();
            RefreshActionMenus();
        }

        private string? GlobalInputDisabledReason(GlobalActionBinding? action, ForegroundHotkeyContext context,
            bool resumeShortcut = false, List<string>? ignoredPrograms = null)
        {
            if (IsClosing || IsConfiguring || GlobalInputCapture.Active) return "HotkeyCapturingInput";
            if (action is not null && GlobalActionUnavailableReasonKey(action.Kind) is { } unavailable)
                return unavailable;
            if (_globalShortcutsSuspended && !resumeShortcut) return "HotkeyManuallySuspended";
            ignoredPrograms ??= Settings.HotkeyPolicy.IgnoredPrograms;
            var needsProgram = ignoredPrograms.Count != 0 || action?.IgnoredPrograms.Count > 0;
            if ((needsProgram && !context.ProgramAvailable) ||
                (Settings.HotkeyPolicy.DisableInFullscreen && !context.FullscreenAvailable))
                return context.UnavailableReason ?? "HotkeyPolicyObserverUnavailable";
            if (Settings.HotkeyPolicy.DisableInFullscreen && context.Fullscreen) return "HotkeyFullscreenDisabled";
            if (ProgramExclusionRules.Matches(ignoredPrograms, context.Program) ||
                (action is not null && ProgramExclusionRules.Matches(action.IgnoredPrograms, context.Program)))
                return "HotkeyExcludedProgram";

            return null;
        }

        private void GlobalInputCaptureChanged()
        {
            if (IsClosing || IsConfiguring) return;
            RegisterHotkeys();
            RegisterGlobalMouse();
        }

        private Func<Task> GuardGlobalInput(Func<Task> action, string shortcut)
        {
            var binding = Settings.GlobalActions.FirstOrDefault(item => item.Enabled && item.Shortcut == shortcut);
            var resumeShortcut = binding?.Kind == GlobalActionKind.ToggleGlobalShortcuts;
            return () => GlobalInputDisabledReason(binding, ReadGlobalInputContext(), resumeShortcut) is null
                ? action()
                : Task.CompletedTask;
        }
    }
}
