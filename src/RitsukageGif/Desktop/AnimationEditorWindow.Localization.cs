using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow
    {
        private bool _languageClosed;
        private object[] _statusArguments = [];
        private string? _statusKey;

        private void RefreshLanguage()
        {
            if (_languageClosed) return;
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshLanguage);
                return;
            }

            var scope = _draftScope.SelectedIndex;
            _draftScope.ItemsSource = DraftScopes();
            _draftScope.SelectedIndex = scope;
            UpdateWindowTitle();
            UpdateFrameSummary();
            if (_statusKey is { } key) _status.Text = UiText.Format(key, _statusArguments);
        }

        private static string[] DraftScopes()
        {
            return [UiText.Get("AnimationCurrentFrame"), UiText.Get("AnimationSelectedFrames")];
        }

        private void UpdateWindowTitle()
        {
            Title =
                $"{UiText.Get(_imageMode ? "ImageEditorTitle" : "AnimationEditorTitle")} — {(_path is null ? UiText.Get("EditorNew") : Path.GetFileName(_path))}{(_dirty ? " *" : "")}";
        }

        private void SetStatus(string key, params object[] arguments)
        {
            _statusKey = key;
            _statusArguments = arguments;
            _status.Text = UiText.Format(key, arguments);
        }

        private void SetStatusError(string message)
        {
            _statusKey = null;
            _statusArguments = [];
            _status.Text = message;
        }
    }
}
