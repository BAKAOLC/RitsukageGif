using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PresetEditorWindow
    {
        private readonly TextBlock _recordingProgress = new() { FontSize = 11 };

        private StackPanel CreateProgressFooter()
        {
            var panel = new StackPanel { Spacing = 3 };
            panel.Children.Add(_status);
            panel.Children.Add(_recordingProgress);
            _owner.Changed += RefreshRecordingProgress;
            RefreshRecordingProgress();
            return panel;
        }

        private void RefreshRecordingProgress()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshRecordingProgress);
                return;
            }

            if (_allowClose) return;
            _recordingProgress.IsVisible = _owner.LatestProgress is not null;
            _recordingProgress.Text =
                _owner.LatestProgress is { } progress ? RecordingProgressText.Format(progress) : "";
        }
    }
}
