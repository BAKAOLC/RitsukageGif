using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        private readonly Window _owner;
        private readonly PinnedWindowShake _shake = new();
        private bool _visibleInGroup = true;

        internal PinnedWindowPresentation(Window owner, Grid content, Func<PinnedImagePreferences> preferences)
        {
            _owner = owner;
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
            Title.SetTitle(state.CustomTitle);
            GroupHidden = state.GroupHidden;
            _visibleInGroup = state.VisibleInGroup;
        }

        internal void Capture(PinnedWindowState state)
        {
            state.CustomTitle = Title.Value;
            state.GroupHidden = GroupHidden;
            state.VisibleInGroup = VisibleInGroup;
        }
    }
}
