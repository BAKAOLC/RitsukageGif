using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    internal static class NumericChevron
    {
        private static readonly Geometry Increase = IconCatalog.GetGeometry("NumericChevronIncrease");
        private static readonly Geometry Decrease = IconCatalog.GetGeometry("NumericChevronDecrease");

        internal static IControlTemplate SpinnerTemplate { get; } =
            new FuncControlTemplate<ButtonSpinner>((owner, scope) =>
            {
                owner.PropertyChanged -= SpinnerPropertyChanged;
                owner.PropertyChanged += SpinnerPropertyChanged;
                UpdateInputCorners(owner);
                var grid = new Grid { ColumnDefinitions = new("*,Auto") };
                var content = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
                    [!ContentPresenter.ContentTemplateProperty] = owner[!ContentControl.ContentTemplateProperty],
                };
                scope.Register(content.Name, content);
                grid.Children.Add(content);
                var buttons = new Grid { RowDefinitions = new("*,*") };
                foreach (var increase in new[] { true, false })
                {
                    var button = new RepeatButton
                    {
                        Name = increase ? "PART_IncreaseButton" : "PART_DecreaseButton",
                        Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(0),
                        MinWidth = 0, MinHeight = 0, Height = double.NaN,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        [!TemplatedControl.ForegroundProperty] = owner[!TemplatedControl.ForegroundProperty],
                    };
                    button.Content = Create(button, increase);
                    scope.Register(button.Name, button);
                    Grid.SetRow(button, increase ? 0 : 1);
                    buttons.Children.Add(button);
                }

                var buttonSurface = new Border
                {
                    Width = 18, CornerRadius = new(0, 4, 4, 0), ClipToBounds = true, Child = buttons,
                    [!Border.BackgroundProperty] = new DynamicResourceExtension("TextBoxDefaultBackground"),
                    [!Visual.IsVisibleProperty] = owner[!ButtonSpinner.ShowButtonSpinnerProperty],
                };
                Grid.SetColumn(buttonSurface, 1);
                grid.Children.Add(buttonSurface);
                return grid;
            });

        private static void SpinnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == ButtonSpinner.ShowButtonSpinnerProperty && sender is ButtonSpinner spinner)
                UpdateInputCorners(spinner);
        }

        private static void UpdateInputCorners(ButtonSpinner spinner)
        {
            if (spinner.Content is TextBox editor)
                editor.CornerRadius = spinner.ShowButtonSpinner ? new(4, 0, 0, 4) : new(4);
        }

        internal static Canvas Create(TemplatedControl owner, bool increase)
        {
            return new()
            {
                Width = 12, Height = 8,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Path
                    {
                        Data = increase ? Increase : Decrease, Width = 12, Height = 8, StrokeThickness = 1.5,
                        [!Shape.StrokeProperty] = owner[!TemplatedControl.ForegroundProperty],
                    },
                },
            };
        }
    }
}
