using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class StructuredRecognitionWindow
    {
        private readonly List<(FormulaLatexFormat Format, MenuItem Item)> _formulaFormatChoices = [];
        private readonly Button _formulaSettings = new();
        private readonly Action<FormulaLatexFormat>? _latexFormatChanged;
        private FormulaLatexFormat _latexFormat;

        private Button CreateFormulaSettingsButton()
        {
            _formulaSettings.Bind(ContentProperty, LiveText.Resource("Settings"));
            var formatMenu = new MenuItem();
            formatMenu.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource("FormulaLatexFormat"));
            foreach (var format in Enum.GetValues<FormulaLatexFormat>())
            {
                var item = new MenuItem
                {
                    ToggleType = MenuItemToggleType.Radio, GroupName = "formula-latex-format",
                    IsChecked = format == _latexFormat,
                };
                item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource(format switch
                {
                    FormulaLatexFormat.FormulaOnly => "FormulaLatexOnly",
                    FormulaLatexFormat.InlineDollar => "FormulaLatexInlineDollar",
                    FormulaLatexFormat.DisplayDollar => "FormulaLatexDisplayDollar",
                    FormulaLatexFormat.DisplayBrackets => "FormulaLatexDisplayBrackets",
                    FormulaLatexFormat.InlineBrackets => "FormulaLatexInlineBrackets",
                    FormulaLatexFormat.Equation => "FormulaLatexEquation",
                    FormulaLatexFormat.EquationStar => "FormulaLatexEquationStar",
                    _ => throw new ArgumentOutOfRangeException(nameof(format)),
                }));
                item.Click += (_, _) => SetFormulaLatexFormat(format);
                _formulaFormatChoices.Add((format, item));
                formatMenu.Items.Add(item);
            }

            var menu = new MenuFlyout();
            menu.Items.Add(formatMenu);
            _formulaSettings.Flyout = menu;
            return _formulaSettings;
        }

        private void SetFormulaLatexFormat(FormulaLatexFormat format)
        {
            if (_busy || _lifetime.IsCancellationRequested || _latexFormat == format) return;
            _latexFormat = format;
            foreach (var choice in _formulaFormatChoices) choice.Item.IsChecked = choice.Format == format;
            RefreshFormulaEditorText();
            SetBusy(_busy);
            _latexFormatChanged?.Invoke(format);
        }

        private string[] FormulaDisplaySourcesForOutput()
        {
            var index = _formulaList.SelectedIndex - 1;
            return index >= 0 && index < _formulas.Count
                ? [_latex.Text ?? ""]
                : [.. _formulas.Select(formula => FormulaLatexFormatter.Format(formula.Latex, _latexFormat))];
        }
    }
}
