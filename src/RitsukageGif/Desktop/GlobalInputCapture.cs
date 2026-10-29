using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal static class GlobalInputCapture
    {
        private static readonly Lock Gate = new();
        private static readonly HashSet<Control> ActiveOwners = [];
        private static readonly ConditionalWeakTable<Control, GlobalInputCaptureRegistration> Registrations = new();
        private static int _activeCount;

        internal static bool Active => Volatile.Read(ref _activeCount) > 0;

        internal static event Action? Changed;

        internal static void Observe(Control control)
        {
            ArgumentNullException.ThrowIfNull(control);
            _ = Registrations.GetValue(control, static target => new(target));
        }

        internal static void SetActive(Control control, bool active)
        {
            bool changed;
            lock (Gate)
            {
                var previous = ActiveOwners.Count;
                if (active) ActiveOwners.Add(control);
                else ActiveOwners.Remove(control);
                Volatile.Write(ref _activeCount, ActiveOwners.Count);
                changed = previous == 0 != (ActiveOwners.Count == 0);
            }

            if (changed) Changed?.Invoke();
        }
    }
}
