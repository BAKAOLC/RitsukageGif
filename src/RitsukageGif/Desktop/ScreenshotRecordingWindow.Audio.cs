using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private long _audioPanelGeneration;

        private Control BuildAudioSettings()
        {
            var generation = ++_audioPanelGeneration;
            var rows = new StackPanel { Spacing = 4 };
            AddAudioSetting(rows, true, generation);
            AddAudioSetting(rows, false, generation);
            return rows;
        }

        private void AddAudioSetting(StackPanel rows, bool system, long generation)
        {
            var check = new CheckBox
            {
                Content = UiText.Get(system ? "RecordingSystemAudio" : "RecordingMicrophoneAudio"),
                IsChecked = AudioCaptureBackend.IsSupported &&
                            (system ? _controller.SystemAudio : _controller.Microphone),
                IsEnabled = false,
            };
            rows.Children.Add(check);
            check.IsCheckedChanged += (_, _) =>
            {
                if (!_controller.CanConfigure || generation != _audioPanelGeneration) return;
                if (system) _controller.SystemAudio = check.IsChecked == true;
                else _controller.Microphone = check.IsChecked == true;
                _controller.ConfigurationChanged();
            };
            if (!AudioCaptureBackend.IsSupported)
            {
                ToolTip.SetTip(check, UiText.Get("VideoAudioUnavailable"));
                return;
            }

            check.AttachedToVisualTree += async (_, _) =>
            {
                var token = _controller.Lifetime;
                try
                {
                    var available = await Task.Run(() => AudioCaptureBackend.EnumerateDevices(system), token);
                    if (_closed || token.IsCancellationRequested || generation != _audioPanelGeneration ||
                        !_controller.CanConfigure) return;
                    check.IsEnabled = available.Count > 0;
                    if (available.Count == 0)
                    {
                        check.IsChecked = false;
                        ToolTip.SetTip(check,
                            UiText.Get(system ? "RecordingSystemAudioUnavailable" : "RecordingMicrophoneUnavailable"));
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception failure)
                {
                    LogHub.GetLogger("Desktop.ScreenshotRecording").ZLogWarning(failure,
                        $"Audio device enumeration failed. SystemAudio={system}");
                    if (_closed || token.IsCancellationRequested || generation != _audioPanelGeneration) return;
                    check.IsChecked = false;
                    check.IsEnabled = false;
                    ToolTip.SetTip(check, ApplicationHost.VideoErrorMessage(failure));
                }
            };
        }
    }
}
