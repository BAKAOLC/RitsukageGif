namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private AnimationEditorWindow? _animationEditor;

        internal async Task ShowAnimationEditorAsync(string? path = null)
        {
            if (_closing) return;
            if (_animationEditor is { } existing)
            {
                WindowActivation.Show(existing);
                if (path is not null) await existing.OpenPathAsync(path);
                return;
            }

            var window = new AnimationEditorWindow(path);
            if (View is { } view) window.Icon = view.Icon;
            _animationEditor = window;
            window.Closed += (_, _) => _animationEditor = null;
            WindowActivation.Show(window);
        }
    }
}
