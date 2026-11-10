using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class StructuredRecognitionWindow
    {
        private readonly TextBlock _formulaCount = new() { VerticalAlignment = VerticalAlignment.Center };

        private readonly TextBlock _formulaEmpty = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        private readonly ListBox _formulaList = new();
        private readonly List<FormulaResultListItem> _formulaRows = [];
        private readonly List<RecognizedFormula> _formulas = [];
        private TextBlock _allFormulas = new();
        private Control? _formulaCodePane;
        private Grid? _formulaEditorLayout;
        private GridSplitter? _formulaEditorSplitter;
        private Grid? _formulaResultsLayout;
        private Control? _formulaSidebar;
        private GridSplitter? _formulaSidebarSplitter;
        private GridLength _formulaSidebarWidth = new(160);
        private bool _syncingFormulaSelection;

        private Grid CreateFormulaResultsPane()
        {
            _formulaList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
            {
                Setters = { new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) },
            });
            var title = new TextBlock
                { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            title.Bind(TextBlock.TextProperty, LiveText.Resource("FormulaList"));
            _formulaEmpty.Bind(TextBlock.TextProperty, LiveText.Resource("FormulaEmpty"));
            var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Margin = new(4, 0, 4, 6) };
            header.Children.Add(title);
            Grid.SetColumn(_formulaCount, 1);
            header.Children.Add(_formulaCount);
            var sidebar = new Grid { RowDefinitions = new("Auto,*"), MinWidth = 108 };
            sidebar.Children.Add(header);
            Grid.SetRow(_formulaList, 1);
            sidebar.Children.Add(_formulaList);
            ScrollViewer.SetHorizontalScrollBarVisibility(_formulaList, ScrollBarVisibility.Disabled);
            var preview = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 4 };
            var previewTitle = new TextBlock();
            previewTitle.Bind(TextBlock.TextProperty, LiveText.Resource("FormulaCopyImage"));
            preview.Children.Add(previewTitle);
            Grid.SetRow(_preview, 1);
            preview.Children.Add(_preview);
            Grid.SetRow(_formulaEmpty, 1);
            preview.Children.Add(_formulaEmpty);
            var code = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 4 };
            var codeHeader = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 };
            codeHeader.Children.Add(new TextBlock { Text = "LaTeX", VerticalAlignment = VerticalAlignment.Center });
            var hint = new TextBlock
            {
                Opacity = .7, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            hint.Bind(TextBlock.TextProperty, LiveText.Resource("FormulaEditHint"));
            hint.Bind(ToolTip.TipProperty, LiveText.Resource("FormulaEditHint"));
            Grid.SetColumn(hint, 1);
            codeHeader.Children.Add(hint);
            code.Children.Add(codeHeader);
            Grid.SetRow(_latex, 1);
            code.Children.Add(_latex);
            var editor = new Grid { RowDefinitions = new("*,5,*") };
            editor.Children.Add(preview);
            var vertical = new GridSplitter
            {
                Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Rows,
            };
            Grid.SetRow(vertical, 1);
            editor.Children.Add(vertical);
            Grid.SetRow(code, 2);
            editor.Children.Add(code);
            var layout = new Grid { ColumnDefinitions = new("160,5,*") };
            layout.ColumnDefinitions[2].MinWidth = 160;
            layout.Children.Add(sidebar);
            var splitter = new GridSplitter
            {
                Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
            };
            Grid.SetColumn(splitter, 1);
            layout.Children.Add(splitter);
            Grid.SetColumn(editor, 2);
            layout.Children.Add(editor);
            _formulaResultsLayout = layout;
            _formulaSidebar = sidebar;
            _formulaSidebarSplitter = splitter;
            _formulaEditorLayout = editor;
            _formulaEditorSplitter = vertical;
            _formulaCodePane = code;
            _formulaList.SelectionChanged += (_, _) =>
            {
                if (!_syncingFormulaSelection) ShowFormulaSelection();
            };
            _latex.PropertyChanged += (_, change) =>
            {
                if (change.Property != TextBox.TextProperty) return;
                var index = _formulaList.SelectedIndex - 1;
                if (_syncingFormulaSelection || index < 0 || index >= _formulas.Count) return;
                var text = FormulaLatexFormatter.Unwrap(_latex.Text ?? "");
                var changed = _formulas[index].Latex != text;
                _formulas[index] = _formulas[index] with { Latex = text };
                _formulaRows[index].SetLatex(FormulaSource(text));
                if (changed)
                {
                    _renderTimer.Stop();
                    _renderTimer.Start();
                }

                SetBusy(_busy);
            };
            UiText.LanguageChanged += OnFormulaLanguageChanged;
            ActualThemeVariantChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_lifetime.IsCancellationRequested || _formulas.Count == 0) return;
                _renderTimer.Stop();
                _renderWork?.Cancel();
                _renderRevision++;
                _preview.Source = null;
                _formulaBitmap?.Dispose();
                _formulaBitmap = null;
                foreach (var row in _formulaRows) row.RefreshTheme();
                _renderTimer.Start();
            });
            Closed += (_, _) =>
            {
                UiText.LanguageChanged -= OnFormulaLanguageChanged;
                foreach (var row in _formulaRows) row.Dispose();
            };
            ResetFormulaResults();
            return layout;
        }

        private void ApplyFormulaResult(FormulaRecognitionResult result)
        {
            ResetFormulaResults();
            _formulas.AddRange(result.Formulas);
            RebuildFormulaList(_formulas.Count == 1 ? 0 : -1);
        }

        private void ResetFormulaResults()
        {
            _renderTimer.Stop();
            _renderWork?.Cancel();
            _renderRevision++;
            _syncingFormulaSelection = true;
            try
            {
                foreach (var row in _formulaRows) row.Dispose();
                _formulaRows.Clear();
                _formulas.Clear();
                _formulaList.Items.Clear();
                _latex.IsReadOnly = true;
                _latex.Text = "";
                _preview.Source = null;
                _formulaBitmap?.Dispose();
                _formulaBitmap = null;
            }
            finally
            {
                _syncingFormulaSelection = false;
            }

            _renderTimer.Stop();
            RefreshFormulaSidebar();
        }

        private void RebuildFormulaList(int selected)
        {
            _syncingFormulaSelection = true;
            try
            {
                foreach (var row in _formulaRows) row.Dispose();
                _formulaRows.Clear();
                _formulaList.Items.Clear();
                if (_formulas.Count > 0)
                {
                    _allFormulas = new() { Text = UiText.Format("FormulaAll", _formulas.Count) };
                    _formulaList.Items.Add(new ListBoxItem { MinHeight = 30, Content = _allFormulas });
                    for (var index = 0; index < _formulas.Count; index++)
                    {
                        var itemIndex = index;
                        var row = new FormulaResultListItem(FormulaSource(_formulas[index].Latex), index + 1,
                            () => RemoveFormula(itemIndex), _lifetime.Token);
                        _formulaRows.Add(row);
                        _formulaList.Items.Add(row);
                    }

                    _formulaList.SelectedIndex = selected + 1;
                }
            }
            finally
            {
                _syncingFormulaSelection = false;
            }

            RefreshFormulaSidebar();
            ShowFormulaSelection();
        }

        private void ShowFormulaSelection()
        {
            _renderTimer.Stop();
            _renderWork?.Cancel();
            _renderRevision++;
            _preview.Source = null;
            _formulaBitmap?.Dispose();
            _formulaBitmap = null;
            RefreshFormulaEditorText();
            _renderTimer.Stop();
            if (_formulas.Count > 0 && !_lifetime.IsCancellationRequested) _renderTimer.Start();
            SetBusy(_busy);
        }

        private void RefreshFormulaEditorText()
        {
            var index = _formulaList.SelectedIndex - 1;
            _syncingFormulaSelection = true;
            try
            {
                _latex.IsReadOnly = index < 0;
                _latex.Text = index >= 0 && index < _formulas.Count
                    ? FormulaLatexFormatter.Format(FormulaSource(_formulas[index].Latex), _latexFormat)
                    : FormulaLatexFormatter.FormatMany([.. _formulas.Select(formula => FormulaSource(formula.Latex))],
                        _latexFormat);
                _latex.CaretIndex = 0;
                _latex.SelectionStart = _latex.SelectionEnd = 0;
            }
            finally
            {
                _syncingFormulaSelection = false;
            }
        }

        private string[] FormulaSourcesForOutput()
        {
            var index = _formulaList.SelectedIndex - 1;
            return index >= 0 && index < _formulas.Count
                ? [FormulaSource(_formulas[index].Latex)]
                : [.. _formulas.Select(formula => FormulaSource(formula.Latex))];
        }

        private void RemoveFormula(int index)
        {
            if (_busy || _lifetime.IsCancellationRequested || index < 0 || index >= _formulas.Count) return;
            _formulas.RemoveAt(index);
            RebuildFormulaList(Math.Min(index, _formulas.Count - 1));
        }

        private void RefreshFormulaSidebar()
        {
            _formulaCount.Text = _formulas.Count.ToString(CultureInfo.CurrentCulture);
            _formulaEmpty.IsVisible = _formulas.Count == 0;
            var visible = _formulas.Count > 1;
            var wasVisible = _formulaSidebar?.IsVisible == true;
            if (_formulaSidebar is { } sidebar) sidebar.IsVisible = visible;
            if (_formulaSidebarSplitter is { } splitter) splitter.IsVisible = visible;
            _latex.IsVisible = _formulas.Count > 0;
            if (_formulaCodePane is { } code) code.IsVisible = _formulas.Count > 0;
            if (_formulaEditorSplitter is { } divider) divider.IsVisible = _formulas.Count > 0;
            if (_formulaEditorLayout is { } editor)
            {
                editor.RowDefinitions[1].Height = _formulas.Count > 0 ? new(5) : new(0);
                editor.RowDefinitions[2].Height = _formulas.Count > 0 ? new(1, GridUnitType.Star) : new(0);
            }

            if (_formulaResultsLayout is not { } layout) return;
            if (wasVisible && !visible) _formulaSidebarWidth = layout.ColumnDefinitions[0].Width;
            layout.ColumnDefinitions[0].MinWidth = visible ? 108 : 0;
            if (!visible) layout.ColumnDefinitions[0].Width = new(0);
            else if (!wasVisible) layout.ColumnDefinitions[0].Width = _formulaSidebarWidth;
            layout.ColumnDefinitions[1].Width = visible ? new(5) : new(0);
        }

        private void SetFormulaResultsBusy(bool busy)
        {
            _formulaList.IsEnabled = !busy;
            _formulaSettings.IsEnabled = !busy;
        }

        private void OnFormulaLanguageChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_lifetime.IsCancellationRequested) return;
                _allFormulas.Text = UiText.Format("FormulaAll", _formulas.Count);
                foreach (var row in _formulaRows) row.RefreshLanguage();
            });
        }
    }
}
