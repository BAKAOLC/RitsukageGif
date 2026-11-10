using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationEditableCombo : AutoCompleteBox
    {
        private readonly string[] _items;
        private bool _explicitDropDown;

        public AnnotationEditableCombo(IEnumerable<string> items)
        {
            _items = [.. items];
            ItemsSource = _items;
            FilterMode = AutoCompleteFilterMode.None;
            MinimumPrefixLength = 0;
            MinimumPopulateDelay = TimeSpan.Zero;
            IsTextCompletionEnabled = false;
            MaxDropDownHeight = 240;
            Width = 80;
            Height = 28;
            MinHeight = 0;
            Margin = new(2);
            FontSize = 12;
            Foreground = AppTheme.Ink;
            Background = AppTheme.SurfaceTint;
            VerticalAlignment = VerticalAlignment.Center;
            var arrow = new Button
            {
                Width = 20, MinWidth = 0, MinHeight = 0, Padding = new(0),
                Focusable = false, Background = Brushes.Transparent, BorderThickness = new(0),
                Foreground = AppTheme.Ink,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            arrow.Content = DropDownChevron.Create(arrow);
            InnerRightContent = arrow;
            arrow.Click += (_, _) => ToggleDropDown();
            DropDownClosed += (_, _) => _explicitDropDown = false;
        }

        protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

        protected override void OnDropDownOpening(CancelEventArgs e)
        {
            if (!_explicitDropDown) e.Cancel = true;
            base.OnDropDownOpening(e);
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.Key == Key.F4 || args is { Key: Key.Down, KeyModifiers: KeyModifiers.Alt })
            {
                ToggleDropDown();
                args.Handled = true;
                return;
            }

            if (args.Key == Key.Escape && IsDropDownOpen)
            {
                IsDropDownOpen = false;
                args.Handled = true;
                return;
            }

            if (!IsDropDownOpen && args.Key is Key.Up or Key.Down)
            {
                var index = Array.IndexOf(_items, Text);
                var next = Math.Clamp(index + (args.Key == Key.Down ? 1 : -1), 0, _items.Length - 1);
                SetCurrentValue(SelectedItemProperty, _items[next]);
                args.Handled = true;
                return;
            }

            base.OnKeyDown(args);
        }

        private void ToggleDropDown()
        {
            _explicitDropDown = !IsDropDownOpen;
            SetCurrentValue(IsDropDownOpenProperty, _explicitDropDown);
        }
    }
}
