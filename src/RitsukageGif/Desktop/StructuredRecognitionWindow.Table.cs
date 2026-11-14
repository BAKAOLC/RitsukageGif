using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class StructuredRecognitionWindow
    {
        private readonly Button _tableExcel = new();
        private readonly Button _tablePrint = new() { IsEnabled = false };

        private static KeyModifiers CommandModifier =>
            Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ??
            (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

        private void AddTableOutputButtons(StackPanel toolbar)
        {
            _save.Classes.Add("primary");
            var printLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            printLabel.Bind(TextBlock.TextProperty, LiveText.Resource("Print"));
            _tablePrint.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children = { ToolbarIcon.Create(_tablePrint, ToolbarIcon.GetDefinition("Print")), printLabel },
            };
            _tablePrint.Click += (_, _) => _operation = PrintTableAsync(false);
            ToolTip.SetShowOnDisabled(_tablePrint, true);
            BindButton(_tableExcel, "TableOpenExcel", () => _operation = OpenTableExcelAsync(false));
            toolbar.Children.Add(_save);
            toolbar.Children.Add(_tablePrint);
            toolbar.Children.Add(_tableExcel);
            toolbar.Children.Add(_copy);
        }

        private ContextMenu CreateTableMenu()
        {
            var menu = new ContextMenu();
            var changes = new List<(MenuItem Item, Func<TableRecognitionDocument, bool>? Enabled, Key? Gesture)>();
            Change("TableClearCells", document => document.Clear(_table.SelectedRanges));
            var merge = new MenuItem
            {
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("TableMergeCells"),
            };
            merge.Click += (_, _) => _operation = MergeTableAsync();
            changes.Add((merge, document => document.CanMerge(_table.SelectedRanges), null));
            menu.Items.Add(merge);
            Change("TableSplitCells", document => document.Split(_table.SelectedRanges),
                document => document.CanSplit(_table.SelectedRanges));
            menu.Items.Add(new Separator());
            var rows = Group("TableRows");
            var columns = Group("TableColumns");
            Change("TableInsertRowAbove", document => InsertAxis(document, true, false), group: rows, refresh: false);
            Change("TableInsertRowBelow", document => InsertAxis(document, true, true), group: rows, refresh: false);
            Change("TableDeleteRow", document => DeleteAxes(document, true), document => document.Table.Rows > 1, rows,
                false);
            Change("TableInsertColumnLeft", document => InsertAxis(document, false, false), group: columns,
                refresh: false);
            Change("TableInsertColumnRight", document => InsertAxis(document, false, true), group: columns,
                refresh: false);
            Change("TableDeleteColumn", document => DeleteAxes(document, false), document => document.Table.Columns > 1,
                columns, false);
            menu.Items.Add(new Separator());
            Output("ScreenshotCopy", () => OutputAsync(false));
            Output("ScreenshotSave", () => OutputAsync(true));
            Output("TableOpenExcel", () => OpenTableExcelAsync(true));
            AddTablePrintItem(menu);
            menu.Opening += (_, _) =>
            {
                var committed = _table.CommitEdit();
                foreach (var (item, enabled, gesture) in changes)
                {
                    item.IsEnabled = committed && !_busy && _table is { HasSelection: true, Document: { } document } &&
                                     (enabled?.Invoke(document) ?? true);
                    item.InputGesture = gesture is { } key
                        ? new(key, key == Key.Delete ? KeyModifiers.None : CommandModifier)
                        : null;
                }
            };
            return menu;

            void Change(string key, Action<TableRecognitionDocument> change,
                Func<TableRecognitionDocument, bool>? enabled = null, MenuItem? group = null, bool refresh = true)
            {
                var item = new MenuItem();
                item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource(key));
                changes.Add((item, enabled, key == "TableClearCells" ? Key.Delete : null));
                item.Click += (_, _) =>
                {
                    if (_busy || !_table.HasSelection || _table.Document is not { } document) return;
                    if (!_table.CommitEdit()) return;
                    if (enabled?.Invoke(document) == false) return;
                    Remember();
                    change(document);
                    if (refresh) _table.Refresh();
                };
                if (group is null) menu.Items.Add(item);
                else group.Items.Add(item);
            }

            MenuItem Group(string key)
            {
                var group = new MenuItem();
                group.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource(key));
                menu.Items.Add(group);
                changes.Add((group, null, null));
                return group;
            }

            void Output(string key, Func<Task> action)
            {
                var item = new MenuItem();
                item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource(key));
                changes.Add((item, null, key switch
                {
                    "ScreenshotCopy" => Key.C,
                    "ScreenshotSave" => Key.S,
                    _ => null,
                }));
                item.Click += (_, _) => _operation = action();
                menu.Items.Add(item);
            }
        }

        private void InsertAxis(TableRecognitionDocument document, bool vertical, bool after)
        {
            var bounds = _table.SelectionBounds;
            var index = vertical ? after ? bounds.Bottom : bounds.Row : after ? bounds.Right : bounds.Column;
            if (vertical) document.InsertRow(index);
            else document.InsertColumn(index);
            _table.ApplyAxisChange(vertical, index, null);
        }

        private void DeleteAxes(TableRecognitionDocument document, bool vertical)
        {
            var removed = vertical
                ? document.DeleteRows(_table.SelectedRanges)
                : document.DeleteColumns(_table.SelectedRanges);
            _table.ApplyAxisChange(vertical, null, removed);
        }

        private TableRecognitionResult? TableForOutput(bool selectionOnly)
        {
            if (!_table.CommitEdit()) return null;
            return _table.Document is { } document
                ? WithTableOutputStyle(selectionOnly && _table.HasSelection
                    ? document.Extract(_table.SelectedRanges)
                    : document.Table)
                : null;
        }
    }
}
