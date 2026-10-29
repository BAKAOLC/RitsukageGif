using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class SessionShortcutScope : IDisposable
    {
        private static readonly List<SessionShortcutScope> Scopes = [];
        private bool _disposed;
        private bool _nativeEnabled;

        internal SessionShortcutScope(IReadOnlyList<(string Shortcut, Func<Task> Action)> bindings,
            bool nativeEnabled = false)
        {
            Dispatcher.UIThread.VerifyAccess();
            ArgumentNullException.ThrowIfNull(bindings);
            Bindings = Array.AsReadOnly([.. bindings]);
            foreach (var binding in Bindings)
            {
                ArgumentNullException.ThrowIfNull(binding.Shortcut, nameof(bindings));
                ArgumentNullException.ThrowIfNull(binding.Action, nameof(bindings));
            }

            HotkeyService.Validate(Bindings.Select(binding => binding.Shortcut));
            _nativeEnabled = nativeEnabled;
            Scopes.Add(this);
            try
            {
                Changed?.Invoke();
            }
            catch
            {
                Scopes.Remove(this);
                _disposed = true;
                throw;
            }
        }

        internal static SessionShortcutScope? Current
        {
            get
            {
                Dispatcher.UIThread.VerifyAccess();
                return Scopes.Count == 0 ? null : Scopes[^1];
            }
        }

        internal IReadOnlyList<(string Shortcut, Func<Task> Action)> Bindings { get; }

        internal bool NativeEnabled
        {
            get
            {
                Dispatcher.UIThread.VerifyAccess();
                return _nativeEnabled;
            }
            set
            {
                Dispatcher.UIThread.VerifyAccess();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_nativeEnabled == value) return;
                _nativeEnabled = value;
                if (ReferenceEquals(Current, this)) Changed?.Invoke();
            }
        }

        public void Dispose()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            var current = Current;
            _disposed = true;
            Scopes.Remove(this);
            if (ReferenceEquals(current, this)) Changed?.Invoke();
        }

        internal static event Action? Changed;
    }
}
