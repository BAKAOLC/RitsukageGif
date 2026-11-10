using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private Control BuildInputSettings()
        {
            var keyboard = new CheckBox
            {
                Content = UiText.Get("RecordingKeyboardActions"), IsChecked = _controller.RecordKeyboardActions,
                IsEnabled = _controller.CanChangeKeyboardActions,
            };
            var mouse = new CheckBox
            {
                Content = UiText.Get("RecordingMouseActions"), IsChecked = _controller.RecordMouseActions,
                IsEnabled = _controller.CanChangeMouseActions,
            };
            keyboard.IsCheckedChanged += (_, _) =>
            {
                if (!_controller.CanChangeKeyboardActions) return;
                _controller.ConfigureKeyboardActions(keyboard.IsChecked == true);
            };
            mouse.IsCheckedChanged += (_, _) =>
            {
                if (!_controller.CanChangeMouseActions) return;
                _controller.ConfigureMouseActions(mouse.IsChecked == true);
            };
            return new StackPanel
            {
                Spacing = 4, Children = { keyboard, mouse },
            };
        }
    }
}
