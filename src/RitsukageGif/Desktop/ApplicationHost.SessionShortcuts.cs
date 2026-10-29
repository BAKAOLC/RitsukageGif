using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private IDisposable? _sessionShortcutWindowChanges;

        private void InitializeSessionShortcuts()
        {
            if (!_startServices) return;
            SessionShortcutScope.Changed += SessionShortcutsChanged;
            _sessionShortcutWindowChanges = WindowBase.IsActiveProperty.Changed.AddClassHandler<Window>((_, _) =>
            {
                if (SessionShortcutScope.Current is not null) SessionShortcutsChanged();
            });
        }

        private void SessionShortcutsChanged()
        {
            if (!IsClosing && !IsConfiguring) RegisterHotkeys();
        }

        private bool CanUseNativeSessionShortcuts(SessionShortcutScope scope)
        {
            return !IsClosing && !GlobalInputCapture.Active && ReferenceEquals(SessionShortcutScope.Current, scope) &&
                   scope.NativeEnabled && !_desktop.Windows.Any(window => window.IsActive);
        }

        private void AddSessionShortcuts(List<(string Shortcut, Func<Task> Action)> bindings)
        {
            if (SessionShortcutScope.Current is not { } scope) return;
            try
            {
                HotkeyService.Validate(bindings.Select(binding => binding.Shortcut));
            }
            catch (ArgumentException error)
            {
                SetInputConfigurationStatus(InputConfigurationStatusSource.GlobalActions, error.Message);
                bindings.Clear();
            }

            var reserved = new HashSet<(uint Modifiers, uint Key)>();
            foreach (var binding in scope.Bindings) reserved.Add(HotkeyService.Parse(binding.Shortcut));
            bindings.RemoveAll(binding => reserved.Contains(HotkeyService.Parse(binding.Shortcut)));
            if (!CanUseNativeSessionShortcuts(scope)) return;
            foreach (var binding in scope.Bindings)
                bindings.Add(binding with
                {
                    Action = () => CanUseNativeSessionShortcuts(scope) ? binding.Action() : Task.CompletedTask,
                });
        }

        private void StopSessionShortcuts()
        {
            SessionShortcutScope.Changed -= SessionShortcutsChanged;
            _sessionShortcutWindowChanges?.Dispose();
            _sessionShortcutWindowChanges = null;
        }
    }
}
