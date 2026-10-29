using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static Grid SettingsBody((string Label, Control Content)[] pages, string selectedPage,
            Action<string> selected, Button resetAll)
        {
            var initial = Array.FindIndex(pages, page => page.Label == selectedPage);
            if (initial < 0) initial = 0;
            var contents = pages.ToDictionary(page => page.Content, page => SettingsPageContent(page.Content));
            var navigation = new StackPanel { Spacing = 2, Margin = new(8, 10) };
            var title = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold };
            title.Bind(TextBlock.TextProperty, LiveText.Resource(pages[initial].Label));
            var view = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = contents[pages[initial].Content],
            };
            foreach (var page in pages)
            {
                var button = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    MinHeight = 36,
                };
                var label = new TextBlock
                {
                    [!TextBlock.TextProperty] = LiveText.Resource(page.Label), TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var buttonContent = new Grid { ColumnDefinitions = new("18,*"), ColumnSpacing = 8 };
                buttonContent.Children.Add(ToolbarIcon.Create(button, NavigationIcon(page.Label)));
                Grid.SetColumn(label, 1);
                buttonContent.Children.Add(label);
                button.Content = buttonContent;
                button.Classes.Add("navigation");
                button.Classes.Set("selected", page.Content == pages[initial].Content);
                button.Click += (_, _) =>
                {
                    foreach (var entry in navigation.Children.OfType<Button>())
                        entry.Classes.Set("selected", ReferenceEquals(entry, button));
                    title.Bind(TextBlock.TextProperty, LiveText.Resource(page.Label));
                    view.Content = contents[page.Content];
                    selected(page.Label);
                };
                navigation.Children.Add(button);
            }

            var content = new Grid { RowDefinitions = new("Auto,*") };
            content.Children.Add(new Border
            {
                Padding = new(16, 12), BorderBrush = AppTheme.Outline,
                BorderThickness = new(0, 0, 0, 1), Child = title,
            });
            Grid.SetRow(view, 1);
            content.Children.Add(view);
            var body = new Grid { ColumnDefinitions = new("164,*") };
            var sidebar = new Grid { RowDefinitions = new("*,Auto") };
            sidebar.Children.Add(new ScrollViewer
            {
                Content = navigation, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            resetAll.HorizontalAlignment = HorizontalAlignment.Stretch;
            resetAll.Margin = new(8);
            resetAll.Padding = new(6, 6);
            Grid.SetRow(resetAll, 1);
            sidebar.Children.Add(resetAll);
            body.Children.Add(new Border
            {
                Background = AppTheme.Workspace, BorderBrush = AppTheme.Outline,
                BorderThickness = new(0, 0, 1, 0),
                Child = sidebar,
            });
            Grid.SetColumn(content, 1);
            body.Children.Add(content);
            selected(pages[initial].Label);
            return body;
        }

        private static string NavigationIcon(string key)
        {
            return ToolbarIcon.TryGeometry(key) ?? key switch
            {
                "GeneralHeading" => "M3,6 L21,6 M3,12 L21,12 M3,18 L21,18 M8,3 L8,9 M16,9 L16,15 M9,15 L9,21",
                "AppearanceHeading" => "M8,3 L3,6 1,12 6,14 6,22 18,22 18,14 23,12 21,6 16,3 Q12,10 8,3",
                "SettingsGroupCapture" or "ScreenshotHeading" => ToolbarIcon.Geometry("AnimationSelection"),
                "SettingsAnnotationHeading" => "M4,16 L16,4 20,8 8,20 3,21 Z M14,6 L18,10",
                "SettingsPinsHeading" => "M8,3 L16,3 15,9 19,13 13,13 12,22 11,13 5,13 9,9 Z",
                "SettingsTransferHeading" => "M3,7 L17,7 13,3 M17,7 L13,11 M21,17 L7,17 11,13 M7,17 L11,21",
                "AboutTitle" => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,10 L12,18 M12,6 L12,7",
                "PresetsTitle" => "M4,3 L20,3 20,21 4,21 Z M8,7 L16,7 M8,12 L16,12 M8,17 L13,17",
                "SettingsStorageHeading" or "ScreenshotSaveHeading" => ToolbarIcon.Geometry("ImageSave"),
                "GlobalActionsHeading" => "M5,3 L5,21 20,12 Z",
                "GlobalHotkeysHeading" or "SettingsLocalInput" =>
                    "M2,5 L22,5 22,19 2,19 Z M5,9 L7,9 M10,9 L12,9 M15,9 L17,9 M5,13 L7,13 M10,13 L12,13 M15,13 L19,13 M7,16 L17,16",
                "MouseShortcutsHeading" => "M6,10 C6,0 18,0 18,10 L18,16 C18,25 6,25 6,16 Z M12,3 L12,10 M6,10 L18,10",
                "GlobalMouseHeading" =>
                    "M3,7 L3,3 7,3 M17,3 L21,3 21,7 M21,17 L21,21 17,21 M7,21 L3,21 3,17 M9,10 C9,5 15,5 15,10 L15,15 C15,20 9,20 9,15 Z M12,7 L12,11",
                _ => ToolbarIcon.Geometry("EditorOpen"),
            };
        }

        private static Control SettingsPageContent(Control content)
        {
            return content is TabControl or Grid
                ? content
                : new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = content,
                };
        }

        private static Button BindingButton(string label, string geometry)
        {
            var button = new Button
                { Width = 28, Height = 28, Padding = new(5), VerticalAlignment = VerticalAlignment.Center };
            button.Content = ToolbarIcon.Create(button, geometry);
            button.Bind(ToolTip.TipProperty, LiveText.Resource(label));
            button.Bind(AutomationProperties.NameProperty, LiveText.Resource(label));
            return button;
        }

        private static TextBlock SectionHeading(string key)
        {
            return new()
            {
                [!TextBlock.TextProperty] = LiveText.Resource(key),
                FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
                Margin = new(0, 6, 0, 2),
            };
        }

        private static StackPanel SettingsSection(string title, StackPanel fields)
        {
            fields.Margin = new(0);
            fields.Spacing = 4;
            return new()
            {
                Spacing = 6,
                Children = { SectionHeading(title), MouseSettingsCard(fields) },
            };
        }

        private static void WrapSettingsFields(StackPanel page)
        {
            var fields = new StackPanel { Spacing = 4 };
            var controls = page.Children.ToArray();
            page.Children.Clear();
            foreach (var control in controls) fields.Children.Add(control);
            page.Children.Add(MouseSettingsCard(fields));
        }

        private void FitSettingsToScreen()
        {
            var screen = Screens.ScreenFromWindow(this);
            if (screen is null) return;
            var availableWidth = Math.Max(1, screen.WorkingArea.Width / screen.Scaling - 24);
            var availableHeight = Math.Max(1, screen.WorkingArea.Height / screen.Scaling - 48);
            MinWidth = Math.Min(800, availableWidth);
            MinHeight = Math.Min(540, availableHeight);
            Width = Math.Min(Width, availableWidth);
            Height = Math.Min(Height, availableHeight);
        }
    }
}
