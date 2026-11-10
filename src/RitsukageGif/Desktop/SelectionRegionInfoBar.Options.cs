using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionRegionInfoBar
    {
        private bool _refreshingDisplayOptions;

        private StackPanel CreateDisplayOptions()
        {
            var pixels = new RadioButton
                { Content = "px", Name = "SelectionInfoPixels", GroupName = "InformationUnit" };
            var logical = new RadioButton
                { Content = "dp", Name = "SelectionInfoLogical", GroupName = "InformationUnit" };
            var position = new ToggleSwitch { Name = "SelectionInfoPosition", OnContent = null, OffContent = null };
            var unit = new ToggleSwitch { Name = "SelectionInfoUnit", OnContent = null, OffContent = null };
            var content = new StackPanel
            {
                Spacing = 4, MinWidth = 180,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 12,
                        Children = { pixels, logical },
                    },
                    new TextBlock { [!TextBlock.TextProperty] = LiveText.Resource("SelectionInfoLogicalDescription") },
                    DisplayOptionRow(position, "SelectionInfoShowCoordinates"),
                    DisplayOptionRow(unit, "SelectionInfoShowUnit"),
                },
            };
            pixels.IsCheckedChanged += (_, _) => ApplyOptions();
            logical.IsCheckedChanged += (_, _) => ApplyOptions();
            position.IsCheckedChanged += (_, _) => ApplyOptions();
            unit.IsCheckedChanged += (_, _) => ApplyOptions();
            return content;

            void ApplyOptions()
            {
                if (_refreshingDisplayOptions) return;
                var items = _coordinator.CapturePreferences.InformationItems;
                items = position.IsChecked == true
                    ? items | SelectionInfoItems.Coordinates
                    : items & ~SelectionInfoItems.Coordinates;
                items = unit.IsChecked == true ? items | SelectionInfoItems.Unit : items & ~SelectionInfoItems.Unit;
                _coordinator.SetInformationDisplay(logical.IsChecked == true
                    ? SelectionSizeUnit.LogicalPixels
                    : SelectionSizeUnit.Pixels, items);
            }
        }

        private static Grid DisplayOptionRow(ToggleSwitch toggle, string key)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource(key), VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            return row;
        }

        private void RefreshDisplayOptions(Control content)
        {
            _refreshingDisplayOptions = true;
            try
            {
                var panel = (StackPanel)content;
                var choices = (StackPanel)panel.Children[0];
                var preferences = _coordinator.CapturePreferences;
                ((RadioButton)choices.Children[0]).IsChecked = preferences.InformationUnit == SelectionSizeUnit.Pixels;
                ((RadioButton)choices.Children[1]).IsChecked =
                    preferences.InformationUnit == SelectionSizeUnit.LogicalPixels;
                ((ToggleSwitch)((Grid)panel.Children[2]).Children[1]).IsChecked =
                    (preferences.InformationItems & SelectionInfoItems.Coordinates) != 0;
                ((ToggleSwitch)((Grid)panel.Children[3]).Children[1]).IsChecked =
                    (preferences.InformationItems & SelectionInfoItems.Unit) != 0;
            }
            finally
            {
                _refreshingDisplayOptions = false;
            }
        }

        private static string FormatInformation(PixelRect region, SelectionSizeUnit unit, SelectionInfoItems items,
            double density, bool logicalSource, bool measuring)
        {
            if (items == SelectionInfoItems.None) return "";
            var ratio = logicalSource
                ? unit == SelectionSizeUnit.Pixels ? 1 / density : 1
                : unit == SelectionSizeUnit.LogicalPixels
                    ? density
                    : 1;
            var text = new DefaultInterpolatedStringHandler(24, 4, CultureInfo.CurrentCulture);
            var hasText = false;
            if ((items & SelectionInfoItems.Coordinates) != 0)
            {
                text.AppendFormatted(Math.Round(region.X / ratio, MidpointRounding.AwayFromZero), "0");
                text.AppendLiteral(", ");
                text.AppendFormatted(Math.Round(region.Y / ratio, MidpointRounding.AwayFromZero), "0");
                hasText = true;
            }

            if (!measuring && (items & SelectionInfoItems.Size) != 0)
            {
                if (hasText) text.AppendLiteral(" ");
                text.AppendFormatted(Math.Round(region.Width / ratio, MidpointRounding.AwayFromZero), "0");
                text.AppendLiteral(" × ");
                text.AppendFormatted(Math.Round(region.Height / ratio, MidpointRounding.AwayFromZero), "0");
                hasText = true;
            }

            if (!measuring && (items & SelectionInfoItems.Unit) != 0)
            {
                if (hasText) text.AppendLiteral(" ");
                text.AppendLiteral(unit == SelectionSizeUnit.Pixels ? "px" : "dp");
            }

            return text.ToStringAndClear();
        }
    }
}
