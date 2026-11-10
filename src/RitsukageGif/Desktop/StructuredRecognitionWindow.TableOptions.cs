using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class StructuredRecognitionWindow
    {
        private readonly Button _tableSettings = new();

        private Button CreateTableSettingsButton()
        {
            _tableSettings.Bind(ContentProperty, LiveText.Resource("Settings"));
            var menu = new MenuFlyout();
            AddChoices("TableBorderStyle", Enum.GetValues<TableBorderStyle>(), value => value switch
                {
                    TableBorderStyle.None => "TableBordersNone",
                    TableBorderStyle.Outer => "TableBordersOuter",
                    _ => "TableBordersAll",
                }, value => _dialogOptions.Table.Borders == value,
                value => ChangeTableOptions(_dialogOptions.Table with { Borders = value }));
            AddChoices("TableContentAlignment", Enum.GetValues<TableContentAlignment>(), value => value switch
                {
                    TableContentAlignment.Auto => "TableAlignmentAuto",
                    TableContentAlignment.Left => "TableAlignmentLeft",
                    TableContentAlignment.Center => "TableAlignmentCenter",
                    _ => "TableAlignmentRight",
                }, value => _dialogOptions.Table.Alignment == value,
                value => ChangeTableOptions(_dialogOptions.Table with { Alignment = value }));
            _tableSettings.Flyout = menu;
            return _tableSettings;

            void AddChoices<T>(string title, T[] choices, Func<T, string> text, Func<T, bool> selected,
                Action<T> change) where T : struct, Enum
            {
                var group = new MenuItem { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource(title) };
                foreach (var choice in choices)
                {
                    var item = new MenuItem
                    {
                        [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource(text(choice)),
                        ToggleType = MenuItemToggleType.Radio, GroupName = title, IsChecked = selected(choice),
                    };
                    item.Click += (_, _) => change(choice);
                    menu.Opened += (_, _) => item.IsChecked = selected(choice);
                    group.Items.Add(item);
                }

                menu.Items.Add(group);
            }
        }

        private void ChangeTableOptions(TableOutputStyle options)
        {
            if (_busy || _lifetime.IsCancellationRequested || options == _dialogOptions.Table || !_table.CommitEdit())
                return;
            var baseline = _dialogOptions;
            _dialogOptions = baseline with { Table = options };
            _dialogPreferencesChanged?.Invoke(baseline, _dialogOptions);
            _table.ContentAlignment = options.Alignment;
        }

        private TableRecognitionResult WithTableOutputStyle(TableRecognitionResult table)
        {
            return table with { OutputStyle = _dialogOptions.Table };
        }
    }
}
