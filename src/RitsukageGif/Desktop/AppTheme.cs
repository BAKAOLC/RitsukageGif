using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace RitsukageGif.Desktop
{
    internal sealed class AppTheme : Styles
    {
        internal static readonly IBrush Surface = Brush("#FFFFFF");
        internal static readonly IBrush Workspace = Brush("#F5F5F5");
        internal static readonly IBrush SurfaceTint = Brush("#F8F9FA");
        internal static readonly IBrush Outline = Brush("#141C1F23");
        internal static readonly IBrush Ink = Brush("#1C1F23");
        internal static readonly IBrush Muted = Brush("#9E1C1F23");
        internal static readonly IBrush Accent = Brush("#0064FA");
        internal static readonly IBrush AccentSoft = Brush("#EAF5FF");
        internal static readonly IBrush Error = Brush("#B3261E");

        public AppTheme()
        {
            Resources["ButtonDefaultPrimaryForeground"] = Ink;
            Resources["ButtonDefaultSecondaryForeground"] = Ink;
            Resources["ButtonDefaultHeight"] = 28d;
            Resources["ButtonDefaultFontSize"] = 13d;
            Resources["ButtonDefaultPadding"] = new Thickness(8, 4);
            Resources["NumericUpDownDefaultHeight"] = 28d;
            Resources["TextBoxDefaultHeight"] = 28d;
            Resources["ComboBoxDefaultHeight"] = 28d;
            Add(new Style(s => s.OfType<Window>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.FontSizeProperty, 13d),
                    new Setter(TemplatedControl.ForegroundProperty, Ink),
                    new Setter(TemplatedControl.BackgroundProperty, Surface)
                }
            });
            Add(new Style(s => s.OfType<Button>())
            {
                Setters =
                {
                    new Setter(Layoutable.MinHeightProperty, 28d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 4)),
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4))
                }
            });
            foreach (var type in new[] { typeof(TextBox), typeof(ComboBox), typeof(NumericUpDown) })
                Add(new Style(s => s.OfType(type))
                {
                    Setters =
                    {
                        new Setter(Layoutable.MinHeightProperty, 28d),
                        new Setter(TemplatedControl.FontSizeProperty, 13d),
                        new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 3)),
                        new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4))
                    }
                });
            Add(new Style(s => s.OfType<CheckBox>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.FontSizeProperty, 13d), new Setter(Layoutable.MinHeightProperty, 26d)
                }
            });
            Add(new Style(s => s.OfType<TabItem>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.FontSizeProperty, 13d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 5))
                }
            });
            Add(new Style(s => s.OfType<CompactNumberInput>().Class(":focus-within"))
                { Setters = { new Setter(TemplatedControl.BorderBrushProperty, Accent) } });

            foreach (var name in new[] { "tool", "transport", "icon-button", "navigation" })
            {
                Add(new Style(s => s.OfType<Button>().Class(name))
                {
                    Setters =
                    {
                        new Setter(StyledElement.ThemeProperty, new DynamicResourceExtension("BorderlessButton"))
                    }
                });
            }

            Add(new Style(s => s.OfType<Button>().Class("navigation"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 8)),
                    new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Left)
                }
            });
            Add(new Style(s => s.OfType<Button>().Class("navigation").Class("selected"))
            {
                Setters =
                {
                    new Setter(StyledElement.ThemeProperty, new DynamicResourceExtension("OutlineButton"))
                }
            });
            Add(new Style(s => s.OfType<Button>().Class("active-tool"))
            {
                Setters =
                {
                    new Setter(StyledElement.ThemeProperty, new DynamicResourceExtension("OutlineButton"))
                }
            });
            AddMainWindowSurfaces();
            AddPrimaryButtons();
            AddTimeline();
        }

        private void AddMainWindowSurfaces()
        {
            AddMainWindowButtonSurface(null, Surface);
            AddMainWindowButtonSurface(":pointerover", SurfaceTint);
            AddMainWindowButtonSurface(":pressed", Brush("#E4E7EB"));
            AddMainWindowButtonSurface(":disabled", Workspace);
            AddRecordingButtonSurface(null, Error);
            AddRecordingButtonSurface(":pointerover", Brush("#9F211A"));
            AddRecordingButtonSurface(":pressed", Brush("#851B16"));
            AddRecordingButtonSurface(":disabled", Workspace);
            AddMainWindowComboBoxSurface(null, Surface);
            AddMainWindowComboBoxSurface(":pointerover", SurfaceTint);
            AddMainWindowComboBoxSurface(":focus", Surface);
            AddMainWindowComboBoxSurface(":pressed", Brush("#E4E7EB"));
            AddMainWindowComboBoxSurface(":disabled", Workspace);
            foreach (var name in new[] { "active-tool", "selected" })
                Add(new Style(s => s.OfType<Button>().Class(name).Not(x => x.Class(":disabled"))
                    .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
                {
                    Setters = { new Setter(TextBlock.ForegroundProperty, Accent) }
                });
        }

        private void AddMainWindowButtonSurface(string? state, IBrush background)
        {
            Add(new Style(s =>
            {
                var button = s.OfType<Window>().Class("main-window").Descendant().OfType<Button>()
                    .Not(x => x.Class("primary")).Not(x => x.Class("link")).Not(x => x.Class("recording"));
                if (state is not null) button = button.Class(state);
                return button.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            })
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, background),
                    new Setter(TemplatedControl.BorderBrushProperty, Outline)
                }
            });
        }

        private void AddRecordingButtonSurface(string? state, IBrush background)
        {
            Add(new Style(s =>
            {
                var button = s.OfType<Window>().Class("main-window").Descendant().OfType<Button>()
                    .Class("recording");
                if (state != ":disabled") button = button.Not(x => x.Class(":disabled"));
                if (state is not null) button = button.Class(state);
                return button.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            })
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, background),
                    new Setter(TemplatedControl.BorderBrushProperty, background),
                    new Setter(TextBlock.ForegroundProperty, state == ":disabled" ? Muted : Brushes.White)
                }
            });
        }

        private void AddMainWindowComboBoxSurface(string? state, IBrush background)
        {
            Add(new Style(s =>
            {
                var comboBox = s.OfType<Window>().Class("main-window").Descendant().OfType<ComboBox>();
                if (state is not null) comboBox = comboBox.Class(state);
                return comboBox;
            })
            {
                Setters = { new Setter(TemplatedControl.BackgroundProperty, background) }
            });
        }

        private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

        private void AddPrimaryButtons() => Add(new Style(s => s.OfType<Button>().Class("primary"))
            { Setters = { new Setter(StyledElement.ThemeProperty, new DynamicResourceExtension("SolidButton")) } });

        private void AddTimeline()
        {
            Add(new Style(s => s.OfType<ListBox>().Class("animation-timeline").Descendant().OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(2)),
                    new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(1))
                }
            });
            Add(new Style(s =>
                s.OfType<ListBox>().Class("animation-timeline").Descendant().OfType<ListBoxItem>()
                    .Class(":selected"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, AccentSoft),
                    new Setter(TemplatedControl.BorderBrushProperty, Accent),
                    new Setter(TemplatedControl.ForegroundProperty, Accent)
                }
            });
        }
    }
}
