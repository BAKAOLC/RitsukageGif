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
            var navigation = new StackPanel { Spacing = 1, Margin = new(6, 8) };
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
                    MinHeight = 32,
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
                Padding = new(12, 8), BorderBrush = AppTheme.Outline,
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
            resetAll.Margin = new(6);
            resetAll.Padding = new(6, 4);
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

        private static IconDefinition NavigationIcon(string key)
        {
            return ToolbarIcon.FindDefinition(key) ?? key switch
            {
                "GeneralHeading" => ToolbarIcon.GetDefinition("SettingsNavigationGeneralHeading"),
                "AppearanceHeading" => ToolbarIcon.GetDefinition("SettingsNavigationAppearanceHeading"),
                "SettingsGroupCapture" or "ScreenshotHeading" => ToolbarIcon.GetDefinition("AnimationSelection"),
                "SettingsAnnotationHeading" => ToolbarIcon.GetDefinition("SettingsNavigationSettingsAnnotationHeading"),
                "SettingsPinsHeading" => ToolbarIcon.GetDefinition("SettingsNavigationSettingsPinsHeading"),
                "SettingsTransferHeading" => ToolbarIcon.GetDefinition("SettingsNavigationSettingsTransferHeading"),
                "AboutTitle" => ToolbarIcon.GetDefinition("SettingsNavigationAboutTitle"),
                "PresetsTitle" => ToolbarIcon.GetDefinition("SettingsNavigationPresetsTitle"),
                "SettingsStorageHeading" or "ScreenshotSaveHeading" => ToolbarIcon.GetDefinition("ImageSave"),
                "GlobalActionsHeading" => ToolbarIcon.GetDefinition("SettingsNavigationGlobalActionsHeading"),
                "GlobalHotkeysHeading" or "SettingsLocalInput" =>
                    ToolbarIcon.GetDefinition("SettingsNavigationGlobalHotkeysHeading"),
                "MouseShortcutsHeading" => ToolbarIcon.GetDefinition("SettingsNavigationMouseShortcutsHeading"),
                "GlobalMouseHeading" =>
                    ToolbarIcon.GetDefinition("SettingsNavigationGlobalMouseHeading"),
                _ => ToolbarIcon.GetDefinition("EditorOpen"),
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

        private static Button BindingButton(string label, IconDefinition geometry)
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
            fields.Spacing = 2;
            return new()
            {
                Spacing = 4,
                Children = { SectionHeading(title), MouseSettingsCard(fields) },
            };
        }

        private static void WrapSettingsFields(StackPanel page)
        {
            var fields = new StackPanel { Spacing = 2 };
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
