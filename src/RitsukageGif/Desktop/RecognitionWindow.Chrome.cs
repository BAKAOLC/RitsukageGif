using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class RecognitionWindow
    {
        private Grid WithTextTitleBar(Control content)
        {
            WindowDecorations = WindowDecorations.BorderOnly;
            var root = new Grid { RowDefinitions = new("Auto,*") };
            var titleBar = new Grid
            {
                Height = 32, ColumnDefinitions = new("*,Auto"), Background = AppTheme.SurfaceTint,
            };
            var title = new TextBlock
            {
                Margin = new(10, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                [!TextBlock.TextProperty] = this[!TitleProperty],
            };
            titleBar.Children.Add(title);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var topmost = TextCaptionButton("PinnedImageTopmost", ToolbarIcon.GetDefinition("ScreenshotPin"));
            topmost.Name = "RecognitionTopmost";
            topmost.Foreground = Topmost ? AppTheme.Accent : AppTheme.Ink;
            topmost.Click += (_, _) =>
            {
                Topmost = !Topmost;
                topmost.Foreground = Topmost ? AppTheme.Accent : AppTheme.Ink;
                ChangeTextOptions(_dialogOptions with { Topmost = Topmost }, false);
            };
            buttons.Children.Add(topmost);
            var minimize = TextCaptionButton("WindowMinimize",
                ToolbarIcon.GetDefinition("RecognitionCaptionWindowMinimize"));
            minimize.Name = "RecognitionMinimize";
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;
            buttons.Children.Add(minimize);
            var maximize = TextCaptionButton("WindowMaximize",
                ToolbarIcon.GetDefinition("RecognitionCaptionWindowMaximize"));
            maximize.Name = "RecognitionMaximize";
            maximize.Click += (_, _) => ToggleTextMaximized();
            buttons.Children.Add(maximize);
            var close = TextCaptionButton("Close", ToolbarIcon.GetDefinition("RecognitionCaptionClose"));
            close.Name = "RecognitionClose";
            close.Click += (_, _) => Close();
            buttons.Children.Add(close);
            Grid.SetColumn(buttons, 1);
            titleBar.Children.Add(buttons);
            PropertyChanged += (_, change) =>
            {
                if (change.Property != WindowStateProperty) return;
                var maximized = WindowState == WindowState.Maximized;
                maximize.Content = ToolbarIcon.Create(maximize, maximized
                    ? ToolbarIcon.GetDefinition("RecognitionCaptionWindowRestore")
                    : ToolbarIcon.GetDefinition("RecognitionCaptionWindowMaximize"));
                maximize.Bind(ToolTip.TipProperty, LiveText.Resource(maximized ? "WindowRestore" : "WindowMaximize"));
            };
            titleBar.PointerPressed += (_, args) =>
            {
                if (!args.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;
                if (args.ClickCount == 2) ToggleTextMaximized();
                else BeginMoveDrag(args);
                args.Handled = true;
            };
            root.Children.Add(titleBar);
            Grid.SetRow(content, 1);
            root.Children.Add(content);
            return root;
        }

        private void ToggleTextMaximized()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private static Button TextCaptionButton(string label, IconDefinition geometry)
        {
            var button = new Button
            {
                Width = 36, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(8),
                CornerRadius = new(0), Background = Brushes.Transparent, BorderThickness = new(0),
                Foreground = AppTheme.Ink, Focusable = false,
            };
            button.Content = ToolbarIcon.Create(button, geometry);
            button.Bind(ToolTip.TipProperty, LiveText.Resource(label));
            return button;
        }
    }
}
