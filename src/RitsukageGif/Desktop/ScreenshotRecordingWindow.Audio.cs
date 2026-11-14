using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private long _audioPanelGeneration;

        private Control BuildAudioSettings()
        {
            var generation = ++_audioPanelGeneration;
            var system = CreateAudioSetting(true, generation);
            var microphone = CreateAudioSetting(false, generation);
            var rows = new StackPanel { Spacing = 4, Children = { system, microphone } };
            if (!AudioCaptureBackend.IsSupported)
            {
                ToolTip.SetTip(system, UiText.Get("VideoAudioUnavailable"));
                ToolTip.SetTip(microphone, UiText.Get("VideoAudioUnavailable"));
                return rows;
            }

            rows.AttachedToVisualTree += async (_, _) =>
            {
                var token = _controller.Lifetime;
                system.IsEnabled = microphone.IsEnabled = false;
                try
                {
                    var audio = await _controller.ReadAudioReadinessAsync();
                    if (_closed || token.IsCancellationRequested || generation != _audioPanelGeneration ||
                        !_controller.CanConfigure) return;
                    ConfigureAudioSetting(system, true, audio.System);
                    ConfigureAudioSetting(microphone, false, audio.Microphone);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
            };
            return rows;
        }

        private CheckBox CreateAudioSetting(bool system, long generation)
        {
            var check = new CheckBox
            {
                Content = UiText.Get(system ? "RecordingSystemAudio" : "RecordingMicrophoneAudio"),
                IsEnabled = false,
            };
            check.IsCheckedChanged += (_, _) =>
            {
                if (!check.IsEnabled || !_controller.CanConfigure || generation != _audioPanelGeneration) return;
                if (system) _controller.SystemAudio = check.IsChecked == true;
                else _controller.Microphone = check.IsChecked == true;
                _controller.ConfigurationChanged();
            };
            return check;
        }

        private void ConfigureAudioSetting(CheckBox check, bool system,
            ScreenshotRecordingAudioDiscovery.Endpoint endpoint)
        {
            check.IsChecked = endpoint.Available && (system ? _controller.SystemAudio : _controller.Microphone);
            if (endpoint.Error is { } error)
            {
                LogHub.GetLogger("Desktop.ScreenshotRecording").ZLogWarning(error,
                    $"Audio device enumeration failed. SystemAudio={system}");
                ToolTip.SetTip(check, ApplicationHost.VideoErrorMessage(error));
            }
            else
            {
                ToolTip.SetTip(check, endpoint.Available
                    ? null
                    : UiText.Get(system ? "RecordingSystemAudioUnavailable" : "RecordingMicrophoneUnavailable"));
            }

            check.IsEnabled = endpoint.Available;
        }
    }
}
