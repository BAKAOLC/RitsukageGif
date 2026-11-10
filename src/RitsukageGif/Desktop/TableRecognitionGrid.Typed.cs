using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TableRecognitionGrid
    {
        private static readonly Geometry Checkmark = IconCatalog.GetGeometry("TableRecognitionGridTypedCheckmark");

        private readonly ComboBox _booleanEditor = new()
        {
            IsVisible = false, MinWidth = 0, MinHeight = 0, Padding = new(5, 3),
            ItemsSource = new[] { "false", "true" },
        };

        private readonly Border _invalidEditBorder = new()
        {
            IsVisible = false, IsHitTestVisible = false, BorderBrush = AppTheme.Error, BorderThickness = new(2),
        };

        private TableCell? _pressedBoolean;

        private Control ActiveEditor => _editing?.Value.Kind == TableCellValueKind.Boolean ? _booleanEditor : _editor;

        private Rect CheckBounds(TableCell cell)
        {
            var bounds = CellBounds(cell);
            var extent = Math.Min(16, Math.Min(bounds.Width - 10, bounds.Height - 8));
            return new(bounds.X + 5, bounds.Y + (bounds.Height - extent) / 2, Math.Max(0, extent), Math.Max(0, extent));
        }

        private void DrawBoolean(DrawingContext context, TableCell cell)
        {
            var bounds = CheckBounds(cell);
            context.DrawRectangle(cell.Value.Boolean ? AppTheme.Accent : AppTheme.Surface,
                new Pen(AppTheme.Outline), bounds, 2, 2);
            if (!cell.Value.Boolean || bounds.Width <= 0) return;
            using var transform = context.PushTransform(Matrix.CreateScale(bounds.Width / 12, bounds.Height / 12) *
                                                        Matrix.CreateTranslation(bounds.X, bounds.Y));
            context.DrawGeometry(Brushes.White, null, Checkmark);
        }

        private void ToggleBoolean(TableCell cell)
        {
            if (_document is null) return;
            Changing?.Invoke();
            _document.SetValue(cell.Row, cell.Column, new(TableCellValueKind.Boolean, !cell.Value.Boolean));
            EnsureTable(true);
            InvalidateTable();
        }

        private bool TryEditorValue(TableCell cell, out TableCellValue value)
        {
            var text = _editor.Text ?? "";
            value = cell.Value;
            switch (cell.Value.Kind)
            {
                case TableCellValueKind.Text: return true;
                case TableCellValueKind.Boolean:
                    value = new(TableCellValueKind.Boolean, _booleanEditor.SelectedIndex == 1);
                    return true;
                case TableCellValueKind.Int64 when long.TryParse(text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var integer):
                    value = new(TableCellValueKind.Int64, Integer: integer);
                    return true;
                case TableCellValueKind.Number when double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var number) && double.IsFinite(number):
                    value = new(TableCellValueKind.Number, Number: number);
                    return true;
                default:
                    var error = UiText.Get("TableInvalidNumber");
                    _invalidEditBorder.IsVisible = true;
                    ToolTip.SetTip(_editor, error);
                    return false;
            }
        }

        private void OnEditorKeyDown(object? sender, KeyEventArgs e)
        {
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (e.Key)
            {
                case Key.Escape:
                    CommitEdit(false);
                    Focus();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (CommitEdit()) Focus();
                    e.Handled = true;
                    break;
                case Key.Tab:
                    if (CommitEdit())
                    {
                        Focus();
                        MoveTab(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                    }

                    e.Handled = true;
                    break;
            }
        }
    }
}
