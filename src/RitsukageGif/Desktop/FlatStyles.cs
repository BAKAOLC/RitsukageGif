using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace RitsukageGif.Desktop
{
    internal sealed class FlatStyles : Styles
    {
        public FlatStyles()
        {
            var border = new SolidColorBrush(Color.Parse("#CDD3DA"));
            var ink = new SolidColorBrush(Color.Parse("#303640"));
            Add(new Style(s => s.OfType<Window>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.FontSizeProperty, 13d),
                    new Setter(TemplatedControl.ForegroundProperty, ink),
                    new Setter(TemplatedControl.BackgroundProperty, Brushes.White)
                }
            });
            Add(new Style(s => s.OfType<Button>())
            {
                Setters =
                {
                    new Setter(Layoutable.MinHeightProperty, 28d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 4)),
                    new Setter(TemplatedControl.BackgroundProperty, Brushes.White),
                    new Setter(TemplatedControl.BorderBrushProperty, border),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(2)),
                    new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                    new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                    new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Button>((button, _) =>
                        new ContentPresenter
                        {
                            [!ContentPresenter.ContentProperty] = button[!ContentControl.ContentProperty],
                            [!ContentPresenter.BackgroundProperty] = button[!TemplatedControl.BackgroundProperty],
                            [!ContentPresenter.BorderBrushProperty] = button[!TemplatedControl.BorderBrushProperty],
                            [!ContentPresenter.BorderThicknessProperty] =
                                button[!TemplatedControl.BorderThicknessProperty],
                            [!ContentPresenter.CornerRadiusProperty] = button[!TemplatedControl.CornerRadiusProperty],
                            [!ContentPresenter.PaddingProperty] = button[!TemplatedControl.PaddingProperty],
                            [!ContentPresenter.HorizontalContentAlignmentProperty] =
                                button[!ContentControl.HorizontalContentAlignmentProperty],
                            [!ContentPresenter.VerticalContentAlignmentProperty] =
                                button[!ContentControl.VerticalContentAlignmentProperty]
                        }))
                }
            });
            Add(new Style(s => s.OfType<Button>().Class(":pointerover"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#EEF3F8"))),
                    new Setter(TemplatedControl.BorderBrushProperty, new SolidColorBrush(Color.Parse("#8DA2B8")))
                }
            });
            Add(new Style(s => s.OfType<Button>().Class(":pressed"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#DFE8F1")))
                }
            });
            Add(new Style(s => s.OfType<Button>().Class(":focus-visible"))
                { Setters = { new Setter(TemplatedControl.BorderBrushProperty, Brushes.SteelBlue) } });
            Add(new Style(s => s.OfType<Button>().Class(":disabled"))
                { Setters = { new Setter(Visual.OpacityProperty, .45d) } });
            foreach (var type in new[] { typeof(TextBox), typeof(ComboBox), typeof(NumericUpDown) })
                Add(new Style(s => s.OfType(type))
                {
                    Setters =
                    {
                        new Setter(Layoutable.MinHeightProperty, 28d),
                        new Setter(TemplatedControl.PaddingProperty, new Thickness(7, 3)),
                        new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(2)),
                        new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                        new Setter(TemplatedControl.BorderBrushProperty, border),
                        new Setter(TemplatedControl.BackgroundProperty, Brushes.White),
                        new Setter(TemplatedControl.FontSizeProperty, 13d)
                    }
                });
            Add(new Style(s => s.OfType<CheckBox>())
            {
                Setters =
                {
                    new Setter(Layoutable.MinHeightProperty, 26d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                    new Setter(TemplatedControl.FontSizeProperty, 13d)
                }
            });
            Add(new Style(s => s.OfType<CheckBox>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<CheckBox>((box, _) =>
                    {
                        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*") };
                        var indicator = new Border
                        {
                            Width = 16, Height = 16, CornerRadius = new CornerRadius(2),
                            BorderBrush = border, BorderThickness = new Thickness(1), Background = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Center,
                            Child = new TextBlock
                            {
                                Text = "✓", FontSize = 13, Foreground = Brushes.SteelBlue,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                [!Visual.IsVisibleProperty] = box[!ToggleButton.IsCheckedProperty]
                            }
                        };
                        grid.Children.Add(indicator);
                        var label = new ContentPresenter
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            [!ContentPresenter.ContentProperty] = box[!ContentControl.ContentProperty]
                        };
                        Grid.SetColumn(label, 1);
                        grid.Children.Add(label);
                        return grid;
                    }))
                }
            });
            Add(new Style(s => s.OfType<CheckBox>().Class(":focus-visible").Template().OfType<Border>())
                { Setters = { new Setter(Border.BorderBrushProperty, Brushes.SteelBlue) } });
            Add(new Style(s => s.OfType<CheckBox>().Class(":disabled"))
                { Setters = { new Setter(Visual.OpacityProperty, .45d) } });
            Add(new Style(s => s.OfType<ComboBoxItem>())
            {
                Setters =
                {
                    new Setter(Layoutable.MinHeightProperty, 26d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 3))
                }
            });
            Add(new Style(s => s.OfType<Button>().Class("navigation"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                    new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Left),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 8))
                }
            });
            Add(new Style(s => s.OfType<Button>().Class("navigation").Class("selected"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#E3ECF5"))),
                    new Setter(TemplatedControl.ForegroundProperty, new SolidColorBrush(Color.Parse("#245581")))
                }
            });
        }
    }
}
