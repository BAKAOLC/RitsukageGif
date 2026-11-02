using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed class ClearRecordDialog : Window
    {
        private ClearRecordSelection? _selection;

        internal ClearRecordDialog()
        {
            Width = 340;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            Background = AppTheme.Surface;
            this[!TitleProperty] = LiveText.Resource("ClearRecords");
            var screenshots = Option("ClearScreenshotHistory");
            var regions = Option("ClearRegionHistory");
            var pins = Option("ClearPinHistory");
            var current = Option("ClearPinsOnScreen");
            var clear = new Button
            {
                Name = "ClearRecordsApply", [!ContentProperty] = LiveText.Resource("ClearRecordsAction"),
                IsDefault = true, IsEnabled = false,
            };
            var cancel = new Button { [!ContentProperty] = LiveText.Resource("EditorCancel"), IsCancel = true };
            CheckBox[] options = [screenshots, regions, pins, current];
            foreach (var option in options)
                option.IsCheckedChanged += (_, _) => clear.IsEnabled = options.Any(value => value.IsChecked == true);
            clear.Click += (_, _) =>
            {
                _selection = new(screenshots.IsChecked == true, regions.IsChecked == true,
                    pins.IsChecked == true, current.IsChecked == true);
                Close(_selection);
            };
            cancel.Click += (_, _) => Close();
            Content = new StackPanel
            {
                Margin = new(12), Spacing = 4,
                Children =
                {
                    screenshots, regions, pins, current,
                    new StackPanel
                    {
                        Margin = new(0, 8, 0, 0), Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6,
                        Children = { clear, cancel },
                    },
                },
            };
        }

        internal Task<ClearRecordSelection?> ShowAsync(Window? owner)
        {
            WindowStartupLocation =
                owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            if (owner is not null) return ShowDialog<ClearRecordSelection?>(owner);
            var completion =
                new TaskCompletionSource<ClearRecordSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Closed += (_, _) => completion.TrySetResult(_selection);
            Show();
            return completion.Task;
        }

        private static CheckBox Option(string key)
        {
            return new()
            {
                Name = key, [!ContentProperty] = LiveText.Resource(key),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
        }
    }
}
