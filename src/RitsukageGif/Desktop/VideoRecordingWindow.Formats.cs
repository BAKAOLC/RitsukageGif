namespace RitsukageGif.Desktop
{
    internal sealed partial class VideoRecordingWindow
    {
        private bool IsAnimationOutput => _format.SelectedIndex is >= 0 and < 3;
        private VideoContainer SelectedVideoContainer => (VideoContainer)(_format.SelectedIndex - 3);

        internal void SelectVideoOutput()
        {
            if (_operation is { IsCompleted: false }) return;
            var settings = _host.Settings.VideoRecording;
            _format.SelectedIndex = (int)settings.Container + 3;
            _fps.Value = settings.Fps;
            _scale.Value = settings.Divisor;
            _quality.SelectedIndex = Math.Max(0, Array.IndexOf(QualityValues, settings.Quality));
            _directory.Text = string.IsNullOrWhiteSpace(settings.OutputDirectory)
                ? _host.Settings.OutputDirectory
                : settings.OutputDirectory;
            Refresh();
        }

        private void RefreshQualityLabels()
        {
            var selected = _quality.SelectedIndex;
            _quality.ItemsSource = IsAnimationOutput
                ? new[] { UiText.Get("QualityBest"), UiText.Get("QualityBalanced"), UiText.Get("QualityFast") }
                : new[]
                {
                    UiText.Get("VideoQualityHigh"), UiText.Get("VideoQualityBalanced"), UiText.Get("VideoQualitySmall"),
                };
            _quality.SelectedIndex = selected < 0 ? 1 : selected;
        }

        private void FormatChanged()
        {
            RefreshQualityLabels();
            Refresh();
            if (!IsAnimationOutput && _capabilities is null && !_preparing)
                _ = PrepareAsync(_host.Settings.VideoRecording);
        }
    }
}
