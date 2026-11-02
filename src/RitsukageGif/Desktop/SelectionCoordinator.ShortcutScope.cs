using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private SessionShortcutScope CreateSelectionShortcutScope()
        {
            var reserved = new HashSet<(uint Modifiers, uint Key)>();
            var bindings = new List<(string Shortcut, Func<Task> Action)>();
            foreach (var definition in ShortcutBindings)
            {
                if (definition.Key is Key.None or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
                    or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) continue;
                Reserve(new KeyGesture(definition.Key, definition.Modifiers).ToString());
            }

            return new(bindings, builtinKeys: true);

            void Reserve(string shortcut)
            {
                try
                {
                    if (reserved.Add(HotkeyService.Parse(shortcut)))
                        bindings.Add((shortcut, static () => Task.CompletedTask));
                }
                catch (ArgumentException)
                {
                    // A key unsupported by global registration cannot collide with a global binding.
                }
            }
        }
    }
}
