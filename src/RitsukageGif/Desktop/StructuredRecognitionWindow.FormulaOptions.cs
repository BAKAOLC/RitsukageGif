using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class StructuredRecognitionWindow
    {
        private readonly Action<RecognitionDialogPreferences, RecognitionDialogPreferences>? _dialogPreferencesChanged;
        private RecognitionDialogPreferences _dialogOptions;

        private static MenuItem FormulaCheck(string key, bool selected, Action<bool> changed)
        {
            var item = new MenuItem { ToggleType = MenuItemToggleType.CheckBox, IsChecked = selected };
            item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource(key));
            item.Click += (_, _) => changed(item.IsChecked);
            return item;
        }

        private void ChangeFormulaOptions(FormulaDialogPreferences options, bool refresh = false)
        {
            if (_lifetime.IsCancellationRequested || options == _dialogOptions.Formula) return;
            var baseline = _dialogOptions;
            _dialogOptions = baseline with { Formula = options };
            _dialogPreferencesChanged?.Invoke(baseline, _dialogOptions);
            if (!refresh) return;
            RefreshFormulaEditorText();
            for (var index = 0; index < _formulaRows.Count; index++)
                _formulaRows[index].SetLatex(FormulaSource(_formulas[index].Latex));
            _renderTimer.Stop();
            _renderTimer.Start();
        }

        private string FormulaSource(string source)
        {
            return _dialogOptions.Formula.RemoveSpaces ? LatexTextNormalizer.RemoveInsignificantSpaces(source) : source;
        }

        private void OnFormulaCopyShortcut(object? sender, KeyEventArgs e)
        {
            if (_busy || e.Handled || !_latex.IsFocused || e.Key != Key.C || e.KeyModifiers != CommandModifier) return;
            _operation = OutputAsync(false);
            e.Handled = true;
        }

        private void CompleteFormulaCopy(bool closeAfterCopy)
        {
            if (_lifetime.IsCancellationRequested) return;
            if (closeAfterCopy) Close();
        }

        private void CloseOtherFormulaWindows()
        {
            if (!_formula || !_dialogOptions.Formula.SingleWindowMode ||
                Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
            foreach (var window in desktop.Windows.OfType<StructuredRecognitionWindow>().ToArray())
                if (window._formula && !ReferenceEquals(window, this))
                    window.Close();
        }
    }
}
