using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        private readonly Window _owner;
        private readonly PinnedWindowShake _shake = new();
        private bool _visibleInGroup = true;

        internal PinnedWindowPresentation(Window owner, Grid content, Func<PinnedImagePreferences> preferences,
            Func<Func<Task>, Task> runOperation, TextBlock status)
        {
            _owner = owner;
            InitializeShade(content, preferences);
            _runOperation = runOperation;
            _status = status;
            _messageTimer.Tick += (_, _) =>
            {
                _messageTimer.Stop();
                if (_status.Text == _message) _status.IsVisible = false;
            };
            owner.Closed += (_, _) => _messageTimer.Stop();
            Title = new(owner, content, preferences);
            content.Children.Add(Title);
            Attention = new(owner, content, () => preferences().TranslucentOnAttention);
            owner.PositionChanged += (_, _) =>
            {
                if (owner is { IsVisible: true, IsActive: true } && preferences().HideOthersOnShake &&
                    _shake.Observe(owner.Position.X / owner.RenderScaling, Environment.TickCount64))
                    HideOthersRequested?.Invoke();
            };
        }

        internal long RegistrationOrder { get; set; }
        internal PinnedWindowTitle Title { get; }
        internal PinnedWindowAttention Attention { get; }
        private bool GroupHidden { get; set; }

        private bool VisibleInGroup => GroupHidden ? _visibleInGroup : _owner.IsVisible;
        internal Action? HideOthersRequested { get; set; }
        internal Action? SwitchNextRequested { get; set; }
        internal Func<Task>? MoveGroupRequested { get; set; }

        internal bool Execute(string action)
        {
            switch (action)
            {
                case "window-shade": SetWindowShadow(!WindowShadow); break;
                case "configuration":
                    _ = ConfigurationRequested?.Invoke();
                    break;
                case "paste":
                    _ = RunOperationAsync(() => PasteRequested?.Invoke() ?? Task.CompletedTask);
                    break;
                case "set-title": Title.BeginEdit(); break;
                case "hide-others": HideOthersRequested?.Invoke(); break;
                case "switch-next": SwitchNextRequested?.Invoke(); break;
                case "new-group" when MoveGroupRequested is { } move: _ = move(); break;
                default: return false;
            }

            return true;
        }

        internal void HideForGroup(bool? visible = null)
        {
            if (!GroupHidden) _visibleInGroup = visible ?? _owner.IsVisible;
            GroupHidden = true;
            _owner.Hide();
        }

        internal void RevealGroup()
        {
            if (!GroupHidden) return;
            GroupHidden = false;
            if (_visibleInGroup) _owner.Show();
        }

        internal void SetUserVisibility(bool visible)
        {
            if (!GroupHidden) _visibleInGroup = visible;
        }

        internal void SetGroupIntent(bool visible)
        {
            _visibleInGroup = visible;
        }

        internal void Restore(PinnedWindowState state)
        {
            SetWindowShape(state.WindowShape);
            ReinitializeShadeAfterDataLoad(state.WindowShadow);
            Title.SetTitle(state.CustomTitle);
            SourceMime = state.SourceMime;
            GroupHidden = state.GroupHidden;
            _visibleInGroup = state.VisibleInGroup;
        }

        internal void Capture(PinnedWindowState state)
        {
            state.WindowShadow = WindowShadow;
            state.WindowShape = WindowShape;
            state.CustomTitle = Title.Value;
            state.SourceMime = SourceMime;
            state.GroupHidden = GroupHidden;
            state.VisibleInGroup = VisibleInGroup;
        }
    }
}
