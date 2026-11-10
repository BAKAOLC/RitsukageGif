using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecordingReviewWindow
    {
        private void BuildToolbar()
        {
            var grip = new Button { Width = 20, Height = 30, Padding = new(2) };
            grip.Content = ToolbarIcon.Create(grip, ToolbarIcon.GetDefinition("ToolbarMove"));
            grip.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(args);
            };
            ConfigureIcon(_play, "AnimationPlay", "RecordingReviewPlay", "Space");
            _play.Click += (_, _) => Run(() => SetPlayingAsync(!_playing));
            _actions.Children.Add(_play);
            _actions.Children.Add(_time);
            _actions.Children.Add(new Border { Width = 1, Margin = new(4, 6), Background = AppTheme.Outline });
            _speed.MinHeight = 28;
            ToolTip.SetTip(_speed, UiText.Get("RecordingReviewSpeed"));
            var speeds = new ListBox { MinWidth = 80 };
            var speedPanel = AttachPropertyPanel(_speed, speeds);
            foreach (var rate in new[] { 0.2, 0.5, 0.8, 1d, 1.2, 1.5, 1.8, 2 })
            {
                var item = new ListBoxItem
                {
                    Content = rate.ToString("0.#", CultureInfo.InvariantCulture) + "×",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Tag = rate,
                };
                speeds.Items.Add(item);
                if (rate.Equals(_rate)) speeds.SelectedItem = item;
            }

            speeds.SelectionChanged += (_, _) =>
            {
                if (speeds.SelectedItem is not ListBoxItem { Tag: double rate }) return;
                speedPanel.Hide();
                Run(() => ChangeSpeedAsync(rate));
            };
            speeds.PointerReleased += (_, _) => speedPanel.Hide();
            _actions.Children.Add(_speed);
            ConfigureIcon(_audioButton, "SpeechSpeak", "RecordingReviewAudio");
            _audioButton.IsEnabled = false;
            _actions.Children.Add(_audioButton);
            AddInputControls(_actions);
            ConfigureIcon(_format, _outputFormat == "mp4" ? "ScreenshotRecord" : "AnimationAddImage",
                "RecordingReviewFormat");
            ToolTip.SetTip(_format, _outputFormat.ToUpperInvariant());
            var formats = new ListBox();
            var formatPanel = AttachPropertyPanel(_format, formats);
            foreach (var format in new[] { "gif", "webp", "mp4" })
            {
                var item = new ListBoxItem { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = format };
                var content = new StackPanel { Spacing = 2 };
                content.Children.Add(new TextBlock { Text = format.ToUpperInvariant() });
                content.Children.Add(new TextBlock
                    { Text = UiText.Get("RecordingReviewFormat" + format.ToUpperInvariant()), Opacity = 0.7 });
                item.Content = content;
                formats.Items.Add(item);
                if (format == _outputFormat) formats.SelectedItem = item;
            }

            formats.SelectionChanged += (_, _) =>
            {
                if (formats.SelectedItem is not ListBoxItem { Tag: string format }) return;
                _outputFormat = format;
                _document.FormatChanged?.Invoke(format switch
                {
                    "gif" => RecordingReviewFormat.Gif, "webp" => RecordingReviewFormat.WebP,
                    _ => RecordingReviewFormat.Mp4,
                });
                _format.Content = ToolbarIcon.Create(_format,
                    ToolbarIcon.GetDefinition(format == "mp4" ? "ScreenshotRecord" : "AnimationAddImage"));
                ToolTip.SetTip(_format, format.ToUpperInvariant());
                formatPanel.Hide();
            };
            formats.PointerReleased += (_, _) => formatPanel.Hide();
            _actions.Children.Add(_format);
            ConfigureIcon(_closeButton, "Cancel", "RecordingReviewClose", "Esc");
            _closeButton.Click += (_, _) => Close();
            _actions.Children.Add(_closeButton);
            var save = new Button();
            ConfigureIcon(save, "ScreenshotSave", "RecordingReviewSave", "Ctrl+S");
            var quality = new StackPanel { Spacing = 4, MinWidth = 160 };
            quality.Children.Add(
                new TextBlock { Text = UiText.Get("RecordingReviewQuality"), Margin = new(0, 0, 0, 4) });
            var qualityFlyout = new Flyout { Content = quality };
            foreach (var option in new[]
                     {
                         (Key: "RecordingReviewQualityEconomy", Value: RecordingReviewQuality.Economy),
                         (Key: "RecordingReviewQualityStandard", Value: RecordingReviewQuality.Standard),
                         (Key: "RecordingReviewQualityOriginal", Value: RecordingReviewQuality.Original),
                     })
            {
                var item = new RadioButton
                {
                    Content = UiText.Get(option.Key), GroupName = "ReviewExportQuality",
                    IsChecked = option.Value == _quality,
                };
                item.Click += (_, _) =>
                {
                    _quality = option.Value;
                    _document.QualityChanged?.Invoke(_quality);
                    qualityFlyout.Hide();
                    Run(() => SaveAsync(false));
                };
                quality.Children.Add(item);
            }

            save.Flyout = qualityFlyout;
            _actions.Children.Add(save);
            _actions.Children.Add(IconButton("ScrollCaptureQuickSave", "RecordingReviewQuickSave",
                () => SaveAsync(true), "Ctrl+Shift+S"));
            _actions.Children.Add(IconButton("ScreenshotCopy", "RecordingReviewCopy", CopyAsync, "Ctrl+C"));
            _actions.Children.Add(grip);
            _actions.Children.Add(IconButton("AnimationRedo", "RecordingReviewRerecord",
                () =>
                {
                    _result = RecordingReviewResult.Rerecord;
                    Close();
                    return Task.CompletedTask;
                }));
            var body = new StackPanel { Spacing = 0, Children = { _actions, _status, _timeline } };
            _toolbar.Child = new Border
            {
                Background = AppTheme.Surface, BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                CornerRadius = new(6), Padding = new(4), Child = body,
            };
            _toolbar.PlacementTarget = _preview;
            _toolbar.Placement = PlacementMode.Bottom;
            _toolbar.VerticalOffset = 6;
            _toolbar.IsLightDismissEnabled = false;
            _toolbar.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipX |
                                                     PopupPositionerConstraintAdjustment.FlipY |
                                                     PopupPositionerConstraintAdjustment.SlideX |
                                                     PopupPositionerConstraintAdjustment.SlideY;
            ((Grid)((Border)Content!).Child!).Children.Add(_toolbar);
        }

        private void SetActionsEnabled(bool enabled)
        {
            foreach (var control in _actions.Children)
                control.IsEnabled = enabled || ReferenceEquals(control, _closeButton);
        }

        private Button IconButton(string icon, string key, Func<Task> action, string? gesture = null)
        {
            var button = new Button();
            ConfigureIcon(button, icon, key, gesture);
            button.Click += (_, _) => Run(action);
            return button;
        }

        private static void ConfigureIcon(Button button, string icon, string key, string? gesture = null)
        {
            button.Width = button.Height = 30;
            button.Padding = new(5);
            button.Content = ToolbarIcon.Create(button, ToolbarIcon.GetDefinition(icon));
            var tip = new StackPanel { Spacing = 2 };
            tip.Children.Add(new TextBlock
                { Text = UiText.Get(key), HorizontalAlignment = HorizontalAlignment.Center });
            if (gesture is not null)
                tip.Children.Add(new TextBlock
                    { Text = gesture, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7 });
            ToolTip.SetTip(button, tip);
        }

        private async Task ChangeSpeedAsync(double rate)
        {
            if (_source is null) return;
            _position = CurrentPosition();
            _rate = rate;
            _speed.Content = rate.ToString("0.#", CultureInfo.InvariantCulture) + "×";
            if (_audio is not null)
                try
                {
                    await _audio.SetRateAsync(_rate, _lifetime.Token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    AudioFailed(error);
                }

            _playStart = _position;
            _clock.Restart();
        }
    }
}
