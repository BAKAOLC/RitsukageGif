using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class SessionShortcutScope : IDisposable
    {
        private static readonly List<SessionShortcutScope> Scopes = [];
        private readonly bool _builtinKeys;
        private readonly IReadOnlyList<(string Shortcut, Func<Task> Action)> _originalBindings;
        private bool _disposed;
        private bool _nativeEnabled;

        static SessionShortcutScope()
        {
            SelectionCommands.BindingsChanged += RefreshBuiltinScopes;
        }

        internal SessionShortcutScope(IReadOnlyList<(string Shortcut, Func<Task> Action)> bindings,
            bool nativeEnabled = false, bool builtinKeys = false)
        {
            Dispatcher.UIThread.VerifyAccess();
            ArgumentNullException.ThrowIfNull(bindings);
            _builtinKeys = builtinKeys;
            _originalBindings = Array.AsReadOnly([.. bindings]);
            foreach (var binding in _originalBindings)
            {
                ArgumentNullException.ThrowIfNull(binding.Shortcut, nameof(bindings));
                ArgumentNullException.ThrowIfNull(binding.Action, nameof(bindings));
            }

            Bindings = PrepareBindings();

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

        internal IReadOnlyList<(string Shortcut, Func<Task> Action)> Bindings { get; private set; }

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

        private IReadOnlyList<(string Shortcut, Func<Task> Action)> PrepareBindings()
        {
            if (!_builtinKeys) return _originalBindings;
            var result = new List<(string Shortcut, Func<Task> Action)>();
            var used = new HashSet<(uint Modifiers, uint Key)>();
            foreach (var binding in _originalBindings)
            foreach (var gesture in BuiltinKeys.NativeGestures(KeyGestureDisplay.Parse(binding.Shortcut)))
            {
                var shortcut = gesture.ToString();
                if (used.Add(HotkeyService.Parse(shortcut))) result.Add((shortcut, binding.Action));
            }

            return result.AsReadOnly();
        }

        private static void RefreshBuiltinScopes()
        {
            Dispatcher.UIThread.VerifyAccess();
            var changed = false;
            foreach (var scope in Scopes)
            {
                if (!scope._builtinKeys) continue;
                scope.Bindings = scope.PrepareBindings();
                changed = true;
            }

            if (changed) Changed?.Invoke();
        }

        internal static event Action? Changed;
    }
}
