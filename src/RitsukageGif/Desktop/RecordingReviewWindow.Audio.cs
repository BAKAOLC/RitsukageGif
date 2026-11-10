using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecordingReviewWindow
    {
        private readonly Dictionary<int, RecordingAudioTrackMix> _pendingMix = [];
        private Task _mixing = Task.CompletedTask;
        private HoverPropertyPanel? _audioPanel;
        private bool _audioBuffering;

        private void BuildAudioControls()
        {
            var panel = new StackPanel { Spacing = 8, MinWidth = 220 };
            if (_audio is not null)
                foreach (var track in _audio.Tracks.Where(track =>
                             track.Kind is VideoAudioTrackKind.SystemAudio or VideoAudioTrackKind.Microphone))
                {
                    var mix = _audio.Mix.Single(item => item.StreamIndex == track.StreamIndex);
                    panel.Children.Add(new TextBlock
                    {
                        Text = UiText.Get(track.Kind == VideoAudioTrackKind.SystemAudio
                            ? "RecordingReviewSystemAudio"
                            : "RecordingReviewMicrophone"),
                    });
                    var mute = new ToggleButton
                        { Width = 26, Height = 26, MinHeight = 0, Padding = new(4), IsChecked = mix.Muted };
                    mute.Content = ToolbarIcon.Create(mute,
                        ToolbarIcon.GetDefinition(mix.Muted ? "RecordingReviewMute" : "SpeechSpeak"));
                    ToolTip.SetTip(mute, UiText.Get("RecordingReviewMute"));
                    var gain = new Slider
                    {
                        Minimum = 0, Maximum = 500, Value = mix.Gain * 100,
                        Width = 145, TickFrequency = 1, IsSnapToTickEnabled = true,
                    };
                    var value = new TextBlock
                    {
                        Text = gain.Value.ToString("0", CultureInfo.InvariantCulture) + "%",
                        MinWidth = 36, TextAlignment = TextAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    gain.PropertyChanged += (_, args) =>
                    {
                        if (args.Property == RangeBase.ValueProperty) Changed();
                    };
                    mute.IsCheckedChanged += (_, _) =>
                    {
                        mute.Content = ToolbarIcon.Create(mute,
                            ToolbarIcon.GetDefinition(mute.IsChecked == true ? "RecordingReviewMute" : "SpeechSpeak"));
                        Changed();
                    };
                    panel.Children.Add(new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        Children = { mute, gain, value },
                    });
                    continue;

                    void Changed()
                    {
                        value.Text = gain.Value.ToString("0", CultureInfo.InvariantCulture) + "%";
                        QueueMix(new(track.StreamIndex, mute.IsChecked == true, gain.Value / 100));
                    }
                }

            if (_audioPanel is not null)
            {
                _audioPanel.Dispose();
                _propertyPanels.Remove(_audioPanel);
            }

            _audioPanel = AttachPropertyPanel(_audioButton, panel);
            _audioButton.IsEnabled = _audio is { HasSystemAudio: true } or { HasMicrophone: true };
        }

        private void QueueMix(RecordingAudioTrackMix mix)
        {
            if (_closing) return;
            _pendingMix[mix.StreamIndex] = mix;
            if (_mixing.IsCompleted) _mixing = DrainMixAsync();
        }

        private void AudioFailed(Exception error)
        {
            Logger.ZLogError(error, $"Recording audio playback failed; video playback remains available.");
            _audioFinished = true;
            _playStart = _audio?.Position ?? _position;
            _clock.Restart();
            if (!_closing) _status.Text = UiText.Get("RecordingReviewAudioUnavailable");
        }

        private async Task DrainMixAsync()
        {
            try
            {
                while (_pendingMix.Count != 0 && !_closing)
                {
                    var item = _pendingMix.First();
                    _pendingMix.Remove(item.Key);
                    await _audio!.SetTrackMixAsync(item.Key, item.Value.Muted, item.Value.Gain, _lifetime.Token);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                AudioFailed(error);
            }
        }
    }
}
