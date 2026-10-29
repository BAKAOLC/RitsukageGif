using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AppTheme : Styles
    {
        internal static readonly SolidColorBrush Surface = Brush("#FFFFFF");
        internal static readonly SolidColorBrush Workspace = Brush("#F5F5F5");
        internal static readonly SolidColorBrush SurfaceTint = Brush("#F8F9FA");
        internal static readonly SolidColorBrush Outline = Brush("#141C1F23");
        internal static readonly SolidColorBrush Ink = Brush("#1C1F23");
        internal static readonly SolidColorBrush Muted = Brush("#9E1C1F23");
        internal static readonly SolidColorBrush Accent = Brush("#0064FA");
        internal static readonly SolidColorBrush Success = Brush("#2E8B46");
        private static readonly SolidColorBrush AccentSoft = Brush("#EAF5FF");
        internal static readonly SolidColorBrush Error = Brush("#B3261E");

        public AppTheme()
        {
            AddDropDowns();
            Resources["DropDownChevronBrush"] = Muted;
            Resources["ApplicationFont"] = new FontFamily(DefaultFontFamily);
            Resources["ApplicationThemeVariant"] = ThemeVariant.Light;
            Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
            {
                ["CompactInputBackground"] = Brush("#F8F9FA"),
                ["CompactInputForeground"] = Brush("#1C1F23"),
                ["CompactInputBorder"] = Brush("#141C1F23"),
            };
            Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
            {
                ["CompactInputBackground"] = Brush("#2B2D31"),
                ["CompactInputForeground"] = Brush("#F0F1F3"),
                ["CompactInputBorder"] = Brush("#35FFFFFF"),
            };
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
                    new Setter(TopLevel.RequestedThemeVariantProperty,
                        new DynamicResourceExtension("ApplicationThemeVariant")),
                    new Setter(TemplatedControl.FontFamilyProperty,
                        new DynamicResourceExtension("ApplicationFont")),
                    new Setter(TemplatedControl.ForegroundProperty, Ink),
                    new Setter(TemplatedControl.BackgroundProperty, Surface),
                },
            });
            foreach (var popupType in new[] { typeof(PopupRoot), typeof(OverlayPopupHost) })
                Add(new Style(s => s.OfType(popupType))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.FontFamilyProperty,
                            new DynamicResourceExtension("ApplicationFont")),
                    },
                });
            Add(new Style(s => s.OfType<MenuFlyoutPresenter>().Descendant().OfType<MenuItem>())
            {
                Setters =
                {
                    new Setter(Layoutable.MinHeightProperty, 26d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 3)),
                    new Setter(TemplatedControl.FontSizeProperty, 13d),
                },
            });
            Add(new Style(s => s.OfType<Button>())
            {
                Setters =
                {
                    new Setter(Layoutable.MinHeightProperty, 28d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 4)),
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                },
            });
            foreach (var type in new[] { typeof(TextBox), typeof(ComboBox), typeof(NumericUpDown) })
                Add(new Style(s => s.OfType(type))
                {
                    Setters =
                    {
                        new Setter(Layoutable.MinHeightProperty, 28d),
                        new Setter(TemplatedControl.FontSizeProperty, 13d),
                        new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 3)),
                        new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                    },
                });
            Add(new Style(s => s.OfType<CheckBox>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.FontSizeProperty, 13d), new Setter(Layoutable.MinHeightProperty, 26d),
                    new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                },
            });
            Add(new Style(s => s.OfType<TabItem>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.FontSizeProperty, 13d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 5)),
                },
            });
            Add(new Style(s => s.Class("compact-number-input"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty,
                        new DynamicResourceExtension("CompactInputBackground")),
                    new Setter(TemplatedControl.ForegroundProperty,
                        new DynamicResourceExtension("CompactInputForeground")),
                    new Setter(TemplatedControl.BorderBrushProperty,
                        new DynamicResourceExtension("CompactInputBorder")),
                },
            });
            Add(new Style(s => s.OfType<Window>().Class("main-window").Descendant()
                .Class("compact-number-input"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Brushes.White),
                    new Setter(TemplatedControl.ForegroundProperty, Brush("#303640")),
                    new Setter(TemplatedControl.BorderBrushProperty, Brush("#CBD5E1")),
                },
            });
            Add(new Style(s => s.OfType<Window>().Class("main-window").Descendant().OfType<TextBox>())
            {
                Setters = { new Setter(TemplatedControl.ForegroundProperty, Brush("#303640")) },
            });
            Add(new Style(s => s.Class("compact-number-input").Class(":focus-within"))
                { Setters = { new Setter(TemplatedControl.BorderBrushProperty, Accent) } });

            foreach (var name in new[] { "tool", "transport", "icon-button", "navigation" })
                Add(new Style(s => s.OfType<Button>().Class(name))
                {
                    Setters =
                    {
                        new Setter(StyledElement.ThemeProperty, new DynamicResourceExtension("BorderlessButton")),
                    },
                });

            Add(new Style(s => s.OfType<Button>().Class("navigation"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 8)),
                    new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Left),
                },
            });
            Add(new Style(s => s.OfType<Button>().Class("navigation").Class("selected"))
            {
                Setters =
                {
                    new Setter(StyledElement.ThemeProperty, new DynamicResourceExtension("OutlineButton")),
                },
            });
            Add(new Style(s => s.OfType<Button>().Class("active-tool"))
            {
                Setters =
                {
                    new Setter(StyledElement.ThemeProperty, new DynamicResourceExtension("OutlineButton")),
                },
            });
            Add(new Style(s => s.OfType<Window>().Class("settings-window").Descendant().OfType<Button>())
            {
                Setters = { new Setter(TemplatedControl.FontWeightProperty, FontWeight.Normal) },
            });
            Add(new Style(s => s.OfType<Window>().Class("settings-window").Descendant().OfType<Button>()
                .Class("primary"))
            {
                Setters = { new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold) },
            });
            Add(new Style(s => s.OfType<Window>().Class("settings-window").Descendant().OfType<Button>()
                .Class("navigation"))
            {
                Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 6)) },
            });
            Add(new Style(s => s.OfType<Window>().Class("settings-window").Descendant().OfType<ListBox>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                    new Setter(TemplatedControl.BorderBrushProperty, Outline),
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                },
            });
            Add(new Style(s => s.OfType<Window>().Class("main-window"))
            {
                Setters =
                {
                    new Setter(TopLevel.RequestedThemeVariantProperty, ThemeVariant.Light),
                    new Setter(TemplatedControl.ForegroundProperty, Brush("#303640")),
                    new Setter(TemplatedControl.BackgroundProperty, Brush("#FFFFFF")),
                },
                Resources = { ["DropDownChevronBrush"] = Brush("#7D8188") },
            });
            Add(new Style(s => s.OfType<Window>().Class("main-window").Descendant().OfType<Button>())
            {
                Setters = { new Setter(TemplatedControl.ForegroundProperty, Brush("#303640")) },
                Resources =
                {
                    ["ButtonDefaultPrimaryForeground"] = Brush("#303640"),
                    ["ButtonDefaultSecondaryForeground"] = Brush("#303640"),
                },
            });
            AddMainWindowSurfaces();
            AddPrimaryButtons();
            AddTimeline();
        }

        private void AddMainWindowSurfaces()
        {
            AddMainWindowButtonSurface(null, Brush("#FFFFFF"));
            AddMainWindowButtonSurface(":pointerover", Brush("#EEF3F8"));
            AddMainWindowButtonSurface(":pressed", Brush("#DFE8F1"));
            AddMainWindowButtonSurface(":disabled", Brush("#F5F5F5"));
            AddRecordingButtonSurface(null, Brushes.Red);
            AddRecordingButtonSurface(":pointerover", Brushes.Red);
            AddRecordingButtonSurface(":pressed", Brushes.Red);
            AddRecordingButtonSurface(":disabled", Brush("#F5F5F5"));
            AddMainWindowComboBoxSurface(null, Brush("#FFFFFF"));
            AddMainWindowComboBoxSurface(":pointerover", Brush("#EEF3F8"));
            AddMainWindowComboBoxSurface(":focus", Brush("#FFFFFF"));
            AddMainWindowComboBoxSurface(":pressed", Brush("#DFE8F1"));
            AddMainWindowComboBoxSurface(":disabled", Brush("#F5F5F5"));
            foreach (var name in new[] { "active-tool", "selected" })
                Add(new Style(s => s.OfType<Button>().Class(name).Not(x => x.Class(":disabled"))
                    .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
                {
                    Setters = { new Setter(TextBlock.ForegroundProperty, Accent) },
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
                    new Setter(TemplatedControl.BorderBrushProperty, Brush("#141C1F23")),
                },
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
                    new Setter(TextBlock.ForegroundProperty, state == ":disabled" ? Brush("#9E1C1F23") : Brushes.White),
                },
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
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, background),
                    new Setter(TemplatedControl.ForegroundProperty, Brush("#303640")),
                },
            });
        }

        private static SolidColorBrush Brush(string color)
        {
            return new(Color.Parse(color));
        }

        private void AddPrimaryButtons()
        {
            Add(new Style(s => s.OfType<Button>().Class("primary"))
                { Setters = { new Setter(StyledElement.ThemeProperty, new DynamicResourceExtension("SolidButton")) } });
        }

        private void AddTimeline()
        {
            Add(new Style(s => s.OfType<ListBox>().Class("animation-timeline").Descendant().OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(2)),
                    new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(1)),
                },
            });
            Add(new Style(s =>
                s.OfType<ListBox>().Class("animation-timeline").Descendant().OfType<ListBoxItem>()
                    .Class(":selected"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, AccentSoft),
                    new Setter(TemplatedControl.BorderBrushProperty, Accent),
                    new Setter(TemplatedControl.ForegroundProperty, Accent),
                },
            });
        }
    }
}
