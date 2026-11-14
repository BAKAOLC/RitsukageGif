using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private readonly List<HoverPropertyPanel> _recorderPanels = [];

        private HoverPropertyPanel RegisterRecorderPanel(Control target, Func<Control> create)
        {
            var registered = new HoverPropertyPanel(target, () => new ContentControl(),
                wrapper => { ((ContentControl)wrapper).Content = create(); });
            _recorderPanels.Add(registered);
            return registered;
        }

        private void HideRecorderPanels()
        {
            foreach (var panel in _recorderPanels) panel.Hide();
        }

        private Control BuildRecordingModeSettings()
        {
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock { Text = UiText.Get("RecordingMode") });
            foreach (var quick in new[] { false, true })
            {
                var option = new RadioButton
                {
                    Content = UiText.Get(quick ? "RecordingQuickMode" : "RecordingNormalMode"),
                    GroupName = "RecordingMode", IsChecked = _controller.Quick == quick,
                };
                option.IsCheckedChanged += async (_, _) =>
                {
                    if (option.IsChecked != true || !_controller.CanConfigure) return;
                    HideRecorderPanels();
                    _controller.Quick = quick;
                    _controller.ConfigurationChanged();
                    await GuardAsync(() => _controller.StartAsync(this));
                };
                content.Children.Add(option);
            }

            return content;
        }

        private Control BuildFpsSettings()
        {
            return BuildRecordingChoices([5, 16, 24, 30, 60], _controller.Fps, rate => $"{rate}FPS", rate =>
            {
                _controller.Fps = rate;
                _controller.ConfigurationChanged();
            });
        }

        private Control BuildDelaySettings()
        {
            var title = new TextBlock
                { Text = UiText.Get("RecordingDelay"), HorizontalAlignment = HorizontalAlignment.Center };
            var choices = BuildRecordingChoices([0, 1, 2, 3, 5, 10], _controller.DelaySeconds,
                seconds => seconds == 0 ? UiText.Get("RecordingNoDelay") : $"-{seconds}s", seconds =>
                {
                    _controller.DelaySeconds = seconds;
                    _controller.ConfigurationChanged();
                });
            return new StackPanel { Spacing = 4, Children = { title, choices } };
        }

        private ListBox BuildRecordingChoices(int[] values, int selected, Func<int, string> label, Action<int> choose)
        {
            var list = new ListBox { MinWidth = 76 };
            foreach (var value in values)
            {
                var item = new ListBoxItem
                {
                    Content = label(value), Tag = value, HorizontalContentAlignment = HorizontalAlignment.Center,
                    MinHeight = 28, Padding = new(6, 3),
                };
                list.Items.Add(item);
                if (value == selected) list.SelectedItem = item;
                item.PointerReleased += (_, args) =>
                {
                    if (args.InitialPressMouseButton != MouseButton.Left) return;
                    choose(value);
                    HideRecorderPanels();
                    args.Handled = true;
                };
            }

            return list;
        }
    }
}
