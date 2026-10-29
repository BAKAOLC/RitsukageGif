namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private bool _globalShortcutsSuspended;

        private bool AllowsGlobalInput(bool resumeShortcut = false)
        {
            if (IsClosing || IsConfiguring || GlobalInputCapture.Active) return false;
            if (_globalShortcutsSuspended && !resumeShortcut) return false;
            var policy = Settings.HotkeyPolicy;
            if (policy is { DisableInFullscreen: false, IgnoredPrograms.Count: 0 }) return true;
            if (!OperatingSystem.IsWindows()) return true;
            var foreground =
                ForegroundHotkeyContext.Read(policy.DisableInFullscreen, policy.IgnoredPrograms.Count != 0);
            return (!policy.DisableInFullscreen || !foreground.Fullscreen) && !policy.Ignores(foreground.Program);
        }

        private void GlobalInputCaptureChanged()
        {
            if (IsClosing || IsConfiguring) return;
            RegisterHotkeys();
            RegisterGlobalMouse();
        }

        private Func<Task> GuardGlobalInput(Func<Task> action, string shortcut)
        {
            var resumeShortcut = Settings.GlobalActions.Any(binding => binding is
                                                                       {
                                                                           Enabled: true,
                                                                           Kind: GlobalActionKind.ToggleGlobalShortcuts,
                                                                       } &&
                                                                       binding.Shortcut == shortcut);
            return () => AllowsGlobalInput(resumeShortcut) ? action() : Task.CompletedTask;
        }
    }
}
