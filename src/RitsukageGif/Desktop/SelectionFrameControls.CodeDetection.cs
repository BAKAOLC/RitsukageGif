using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionFrameControls
    {
        private ToggleButton? _codeToggle;

        private void InitializeCodeToggle()
        {
            _codeToggle = Toggle("CaptureCodeToggle", ToolbarIcon.Geometry("RecognizeBarcode"),
                _coordinator.SetCodeDetectionEnabled);
            var tip = new StackPanel { Spacing = 6 };
            tip.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = _codeToggle[!AutomationProperties.NameProperty],
                HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            });
            var gesture = _coordinator.ShortcutBindings.FirstOrDefault(binding =>
                binding.Command == SelectionCommand.ToggleCodeDetection)?.Gesture;
            if (!string.IsNullOrEmpty(gesture))
                tip.Children.Add(new Border
                {
                    BorderBrush = AppTheme.Outline, BorderThickness = new(1), CornerRadius = new(3),
                    Padding = new(5, 2), HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock { Text = KeyGestureDisplay.Format(gesture), FontSize = 11 },
                });
            ToolTip.SetTip(_codeToggle, tip);
            _codeToggle.PointerEntered += (_, _) => ClosePopup();
        }

        private void UpdateCodeToggle()
        {
            if (_codeToggle is null) return;
            _codeToggle.IsVisible = _coordinator.CanShowCodeDetectionToggle;
            _codeToggle.IsChecked = _coordinator.CodeDetectionEnabled;
            _codeToggle.Background = _codeToggle.IsChecked == true ? AppTheme.Accent : AppTheme.Surface;
            _codeToggle.Foreground = _codeToggle.IsChecked == true ? Brushes.White : AppTheme.Ink;
        }
    }
}
