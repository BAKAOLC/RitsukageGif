namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private AnimationEditorWindow? _animationEditor;

        internal async Task ShowAnimationEditorAsync(string? path = null, CancellationToken token = default)
        {
            if (IsClosing) return;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _hostCancellation.Token);
            token = lifetime.Token;
            token.ThrowIfCancellationRequested();
            if (_animationEditor is { } existing)
            {
                WindowActivation.Show(existing);
                if (path is not null) await existing.OpenPathAsync(path, token);
                token.ThrowIfCancellationRequested();
                return;
            }

            var window = new AnimationEditorWindow(path, token);
            if (View is { } view) window.Icon = view.Icon;
            _animationEditor = window;
            window.Closed += (_, _) => _animationEditor = null;
            WindowActivation.Show(window);
            await window.ActiveOperation;
            token.ThrowIfCancellationRequested();
        }
    }
}
