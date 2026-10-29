using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace RitsukageGif.Desktop
{
    public sealed partial class MainWindow
    {
        private readonly Dictionary<Control, (string? Description, string Shortcut)> _actionTips = [];
        private readonly TextBlock _recordLabel = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _sourceLabel = new() { VerticalAlignment = VerticalAlignment.Center };

        private void InitializeActionPresentation()
        {
            Styles.Add(new Style(selector => selector.OfType<Button>().Class("recording"))
            {
                Setters = { new Setter(ForegroundProperty, Brushes.White) },
            });
            _record.Content = _recordLabel;
            _select.Content = _sourceLabel;
            _selectMenu.Click += (_, _) => _select.ContextMenu?.Open(_select);
            _cancel.Bind(ToolTip.TipProperty, LiveText.Resource("MainCancelHint"));
        }

        private async Task RunMainActionAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception error)
            {
                _host.SetStatus(error.Message);
                RefreshFromHost();
            }
        }

        private void RefreshActionPresentation()
        {
            RefreshSourceMenuState();
            _recordLabel.Text =
                UiText.Get(_host.IsRecording ? "StopRecording" : _host.IsBusy ? "Processing" : "StartRecording");
            _sourceLabel.Text = UiText.Get(_host.SelectedCaptureSourceKey);
            SetActionTip(_record, UiText.Get(_host.IsRecording ? "MainStopHint" :
                    _host.Region is null ? "SelectAreaHint" :
                    _host.IsBusy ? "Processing" : "StartRecording"),
                Settings.GetActionShortcut(GlobalActionKind.ToggleRecording));
        }

        private void SetActionTip(Control control, string? description, string shortcut)
        {
            var value = (description, shortcut);
            if (_actionTips.TryGetValue(control, out var previous) && previous == value) return;
            _actionTips[control] = value;
            ToolTip.SetTip(control, WithShortcut(description, shortcut));
        }

        private static object? WithShortcut(string? description, string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) return description;
            return new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap },
                    new Border
                    {
                        Padding = new(5, 2), BorderThickness = new(1), BorderBrush = AppTheme.Outline,
                        CornerRadius = new(3), HorizontalAlignment = HorizontalAlignment.Left,
                        Child = new TextBlock { Text = shortcut },
                    },
                },
            };
        }
    }
}
