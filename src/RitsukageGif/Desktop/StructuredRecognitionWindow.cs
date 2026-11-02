using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class StructuredRecognitionWindow : Window
    {
        private readonly Button _cancel = new();
        private readonly Button _copy = new();
        private readonly Action<FormulaCopyFormat>? _copyFormatChanged;
        private readonly bool _formula;
        private readonly SplitButton _formulaCopy = new();
        private readonly ReadOnlyMemory<byte> _image;
        private readonly Button _install = new();

        private readonly TextBox _latex = new()
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };

        private readonly CancellationTokenSource _lifetime = new();
        private readonly ImageViewport _original = new() { ImageBacking = ImageTransparencyBackground.Checker };
        private readonly ImageViewport _preview = new();
        private readonly Func<CancellationToken, Task>? _recognized;
        private readonly Stack<TableRecognitionHistoryState> _redo = new();
        private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private readonly Button _run = new();
        private readonly Button _save = new();
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TableRecognitionGrid _table = new();
        private readonly Stack<TableRecognitionHistoryState> _undo = new();
        private bool _busy;
        private FormulaCopyFormat _copyFormat;
        private Bitmap? _formulaBitmap;
        private Task _operation = Task.CompletedTask;
        private Bitmap? _originalBitmap;
        private bool _recognitionReported;
        private int _renderRevision;
        private CancellationTokenSource? _renderWork;
        private CancellationTokenSource? _work;

        private StructuredRecognitionWindow(ReadOnlyMemory<byte> image, bool formula,
            Func<CancellationToken, Task>? recognized, Action<Window, IReadOnlyList<string>>? pinFormula,
            FormulaCopyFormat copyFormat, Action<FormulaCopyFormat>? copyFormatChanged,
            bool showOriginalImage, Action<bool>? originalImageVisibilityChanged,
            FormulaLatexFormat latexFormat, Action<FormulaLatexFormat>? latexFormatChanged,
            RecognitionDocumentOutput? documentOutput = null, TableRecognitionResult? initialTable = null)
        {
            showOriginalImage &= initialTable is null;
            _documentOutput = documentOutput ?? RecognitionDocumentOutput.Shared;
            _image = image;
            _formula = formula;
            _recognized = recognized;
            _copyFormat = copyFormat;
            _copyFormatChanged = copyFormatChanged;
            _latexFormat = latexFormat;
            _latexFormatChanged = latexFormatChanged;
            Width = 1000;
            Height = 680;
            MinWidth = 720;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Bind(TitleProperty, LiveText.Resource(initialTable is not null ? "TableEditor" :
                formula ? "FormulaRecognition" : "TableRecognition"));
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            BindButton(_run, "RecognitionRetry", () => _operation = RunAsync());
            BindButton(_cancel, "Cancel", () => _work?.Cancel());
            BindButton(_copy, "ScreenshotCopy", () => _operation = OutputAsync(false));
            _copy.IsVisible = !formula;
            _formulaCopy.IsVisible = formula;
            _formulaCopy.Bind(ContentProperty, LiveText.Resource("ScreenshotCopy"));
            _formulaCopy.Click += (_, _) => _operation = OutputAsync(false);
            _formulaCopy.Flyout = CreateFormulaCopyMenu();
            BindButton(_save, "ScreenshotSave", () => _operation = OutputAsync(true));
            BindButton(_install, "RecognitionInstallComponents", () => _operation = InstallAsync());
            toolbar.Children.Add(_run);
            toolbar.Children.Add(_cancel);
            toolbar.Children.Add(_copy);
            toolbar.Children.Add(_formulaCopy);
            toolbar.Children.Add(_save);
            if (formula) toolbar.Children.Add(CreateFormulaOpenWordButton());
            if (formula && pinFormula is { } pin)
            {
                var pinButton = new Button();
                BindButton(pinButton, "ScreenshotPin", () =>
                {
                    if (_busy || _lifetime.IsCancellationRequested) return;
                    var sources = FormulaSourcesForOutput();
                    if (sources.Length > 0) pin(this, sources);
                });
                toolbar.Children.Add(pinButton);
            }

            var showOriginal = new CheckBox
            {
                IsChecked = showOriginalImage, VerticalAlignment = VerticalAlignment.Center,
                IsVisible = initialTable is null,
            };
            showOriginal.Bind(ContentProperty, LiveText.Resource("RecognitionOriginalImage"));
            toolbar.Children.Add(showOriginal);
            if (formula) toolbar.Children.Add(CreateFormulaSettingsButton());
            else AddTableOutputButtons(toolbar);
            toolbar.Children.Add(_install);
            var originalPane = _original;
            originalPane.IsVisible = showOriginalImage;
            var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
            var panes = new Grid { ColumnDefinitions = new("*,5,*") };
            splitter.IsVisible = showOriginalImage;
            if (!showOriginalImage)
            {
                panes.ColumnDefinitions[0].Width = new(0);
                panes.ColumnDefinitions[1].Width = new(0);
            }

            panes.Children.Add(originalPane);
            Grid.SetColumn(splitter, 1);
            panes.Children.Add(splitter);
            Control results;
            if (formula)
            {
                results = CreateFormulaResultsPane();
                _renderTimer.Tick += async (_, _) =>
                {
                    _renderTimer.Stop();
                    await RenderFormulaAsync();
                };
            }
            else
            {
                results = _table;
                _table.Changing += Remember;
                _table.ContextMenu = CreateTableMenu();
            }

            Grid.SetColumn(results, 2);
            panes.Children.Add(results);
            showOriginal.IsCheckedChanged += (_, _) =>
            {
                originalPane.IsVisible = splitter.IsVisible = showOriginal.IsChecked == true;
                panes.ColumnDefinitions[0].Width = showOriginal.IsChecked == true ? new(1, GridUnitType.Star) : new(0);
                panes.ColumnDefinitions[1].Width = showOriginal.IsChecked == true ? new(5) : new(0);
                originalImageVisibilityChanged?.Invoke(showOriginal.IsChecked == true);
            };
            var layout = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 8, Margin = new(12) };
            layout.Children.Add(toolbar);
            Grid.SetRow(_status, 1);
            layout.Children.Add(_status);
            Grid.SetRow(panes, 2);
            layout.Children.Add(panes);
            Content = layout;
            if (initialTable is null)
            {
                Opened += (_, _) => _operation = RunAsync();
            }
            else
            {
                _table.Document = new(initialTable);
                _run.IsVisible = _cancel.IsVisible = _install.IsVisible = false;
                SetBusy(false);
            }

            Closed += (_, _) =>
            {
                _renderTimer.Stop();
                _lifetime.Cancel();
            };
            AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Bubble, true);
        }

        private static void BindButton(Button button, string key, Action action)
        {
            button.Bind(ContentProperty, LiveText.Resource(key));
            button.Click += (_, _) => action();
        }

        internal static async Task ShowAsync(Window owner, ReadOnlyMemory<byte> image, bool formula,
            Func<CancellationToken, Task>? recognized, CancellationToken token,
            Action<Window, IReadOnlyList<string>>? pinFormula = null,
            FormulaCopyFormat copyFormat = FormulaCopyFormat.Latex, Action<FormulaCopyFormat>? copyFormatChanged = null,
            bool showOriginalImage = true, Action<bool>? originalImageVisibilityChanged = null,
            FormulaLatexFormat latexFormat = FormulaLatexFormat.DisplayDollar,
            Action<FormulaLatexFormat>? latexFormatChanged = null)
        {
            var window = new StructuredRecognitionWindow(image, formula, recognized, pinFormula, copyFormat,
                copyFormatChanged,
                showOriginalImage, originalImageVisibilityChanged, latexFormat, latexFormatChanged);
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            try
            {
                await window.ShowDialog(owner);
            }
            finally
            {
                await window._lifetime.CancelAsync();
                await window._operation;
                window._originalBitmap?.Dispose();
                window._formulaBitmap?.Dispose();
                window._lifetime.Dispose();
            }
        }

        private void Remember()
        {
            if (_table.Document is not { } document) return;
            _undo.Push(new(document.Table, _table.CaptureState()));
            _redo.Clear();
        }

        private async Task RunAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            if (!_table.CommitEdit()) return;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _work = work;
            SetBusy(true);
            _renderTimer.Stop();
            // ReSharper disable once MethodHasAsyncOverload
            _renderWork?.Cancel();
            _renderRevision++;
            if (_formula) ResetFormulaResults();
            _latex.Text = "";
            _preview.Source = null;
            _formulaBitmap?.Dispose();
            _formulaBitmap = null;
            _table.Document = null;
            _undo.Clear();
            _redo.Clear();
            _status.Text = UiText.Get("RecognitionWorking");
            try
            {
                if (_originalBitmap is null)
                {
                    using var input = new MemoryStream(_image.ToArray(), false);
                    _original.Source = _originalBitmap = new(input);
                }

                var service = StructuredRecognitionProcess.Shared;
                var capabilities = _formula
                    ? await ((IFormulaRecognitionService)service).GetCapabilitiesAsync(work.Token)
                    : await ((ITableRecognitionService)service).GetCapabilitiesAsync(work.Token);
                _install.IsVisible = !capabilities.Available;
                if (!capabilities.Available)
                {
                    _status.Text = UiText.Get("RecognitionComponentMissing");
                    ToolTip.SetTip(_status, capabilities.UnavailableReason);
                    return;
                }

                if (_formula)
                {
                    var result = await ((IFormulaRecognitionService)service).RecognizeAsync(_image, work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    ApplyFormulaResult(result);
                    _renderTimer.Stop();
                    await RenderFormulaAsync();
                }
                else
                {
                    var result = await ((ITableRecognitionService)service).RecognizeAsync(_image, work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    _table.Document = new(result);
                    _undo.Clear();
                    _redo.Clear();
                }

                if (!_recognitionReported && _recognized is not null)
                {
                    await _recognized(work.Token);
                    _recognitionReported = true;
                }

                _status.Text = "";
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = UiText.Get("RecognitionCancelled");
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
            }
            finally
            {
                _work = null;
                SetBusy(false);
            }
        }

        private async Task RenderFormulaAsync()
        {
            var revision = ++_renderRevision;
            // ReSharper disable once MethodHasAsyncOverload
            _renderWork?.Cancel();
            _renderWork = null;
            var sources = FormulaSourcesForOutput();
            if (sources.Length == 0)
            {
                _preview.Source = null;
                _formulaBitmap?.Dispose();
                _formulaBitmap = null;
                return;
            }

            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _renderWork = work;
            try
            {
                var color = AppTheme.Ink.Color;
                var rendered = await StructuredRecognitionProcess.Shared.RenderManyPngAsync(sources,
                    (float)(28 * RenderScaling),
                    (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B), work.Token);
                if (_lifetime.IsCancellationRequested || revision != _renderRevision) return;
                if (rendered.ValidationError is { } error)
                {
                    _preview.Source = null;
                    _formulaBitmap?.Dispose();
                    _formulaBitmap = null;
                    _status.Text = error;
                    return;
                }

                using var input = new MemoryStream(rendered.Png!, false);
                var bitmap = new Bitmap(input);
                _preview.Source = bitmap;
                _formulaBitmap?.Dispose();
                _formulaBitmap = bitmap;
                _status.Text = "";
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCancellationRequested && revision == _renderRevision) _status.Text = error.Message;
            }
            finally
            {
                if (ReferenceEquals(_renderWork, work)) _renderWork = null;
            }
        }

        private async Task OutputAsync(bool save)
        {
            if (_busy || (save ? !_save.IsEnabled : !_copy.IsEnabled)) return;
            try
            {
                if (!_table.CommitEdit()) return;
                if (!save)
                {
                    var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
                    if (_formula)
                    {
                        await CopyFormulaAsync();
                        return;
                    }

                    if (_table.Document is { } document)
                    {
                        var result = _table.HasSelection ? document.Extract(_table.SelectedRanges) : document.Table;
                        var transfer = new DataTransfer();
                        var item = DataTransferItem.CreateText(TableRecognitionExport.ToTsv(result));
                        var html = TableRecognitionExport.ToHtml(result);
                        ClipboardPlatformFormats.SetHtml(item, html);
                        if (OperatingSystem.IsWindows())
                            item.Set(DataFormat.CreateBytesPlatformFormat("HTML Format"),
                                ClipboardHtml.CreateWindows(html));
                        transfer.Add(item);
                        await clipboard.SetDataAsync(transfer);
                    }

                    await clipboard.FlushAsync();
                    return;
                }

                var types = _formula
                    ? new[]
                    {
                        new FilePickerFileType("LaTeX") { Patterns = ["*.tex"] },
                        new FilePickerFileType("PNG") { Patterns = ["*.png"] },
                        new FilePickerFileType("SVG") { Patterns = ["*.svg"] },
                        new FilePickerFileType(UiText.Get("FormulaDocxFilter")) { Patterns = ["*.docx"] },
                    }
                    :
                    [
                        new("Excel") { Patterns = ["*.xlsx"] }, new("TSV") { Patterns = ["*.tsv"] },
                        new("HTML") { Patterns = ["*.html"] },
                    ];
                using var file = await StoragePickerAwaiter.WaitAsync(StorageProvider.SaveFilePickerAsync(new()
                {
                    Title = UiText.Get("ScreenshotSave"), DefaultExtension = _formula ? "tex" : "xlsx",
                    SuggestedFileName = _formula ? "formula.tex" : "table.xlsx", FileTypeChoices = types,
                }), _lifetime.Token);
                if (file?.TryGetLocalPath() is not { } path) return;
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (_formula)
                {
                    var sources = FormulaSourcesForOutput();
                    switch (extension)
                    {
                        case ".docx":
                            await SaveFormulaDocxAsync(sources, path);
                            break;
                        case ".png":
                        {
                            var rendered = await StructuredRecognitionProcess.Shared.RenderManyPngAsync(sources,
                                token: _lifetime.Token);
                            if (rendered.ValidationError is { } error)
                            {
                                _status.Text = error;
                                return;
                            }

                            await AtomicFileOutput.WriteBytesAsync(path, rendered.Png!, _lifetime.Token);
                            break;
                        }
                        case ".svg":
                        {
                            var rendered = await StructuredRecognitionProcess.Shared.RenderManySvgAsync(sources,
                                token: _lifetime.Token);
                            if (rendered.ValidationError is { } error)
                            {
                                _status.Text = error;
                                return;
                            }

                            await AtomicFileOutput.WriteTextAsync(path, rendered.Svg!, _lifetime.Token);
                            break;
                        }
                        case ".tex":
                            await AtomicFileOutput.WriteTextAsync(path,
                                string.Join("\n", FormulaDisplaySourcesForOutput()),
                                _lifetime.Token);
                            break;
                        default: throw new NotSupportedException("The formula output format is not supported.");
                    }
                }
                else if (_table.Document is { } document)
                {
                    var result = _table.HasSelection ? document.Extract(_table.SelectedRanges) : document.Table;
                    switch (extension)
                    {
                        case ".tsv":
                            await AtomicFileOutput.WriteTextAsync(path, TableRecognitionExport.ToTsv(result),
                                _lifetime.Token);
                            break;
                        case ".html":
                            await AtomicFileOutput.WriteTextAsync(path, TableRecognitionExport.ToHtml(result),
                                _lifetime.Token);
                            break;
                        case ".xlsx":
                            await TableRecognitionExport.SaveXlsxAsync(result, path, _lifetime.Token);
                            break;
                        default: throw new NotSupportedException("The table output format is not supported.");
                    }
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
            }
        }

        private async Task InstallAsync()
        {
            if (_busy) return;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _work = work;
            SetBusy(true);
            try
            {
                await RecognitionComponentInstaller.InstallAsync(_formula,
                    new Progress<string>(text =>
                    {
                        if (!_lifetime.IsCancellationRequested) _status.Text = text;
                    }), work.Token);
                SetBusy(false);
                await RunAsync();
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = UiText.Get("RecognitionCancelled");
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
            }
            finally
            {
                _work = null;
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _run.IsEnabled = !busy;
            _cancel.IsEnabled = busy;
            _latex.IsEnabled = _table.IsEnabled = !busy;
            _copy.IsEnabled = _save.IsEnabled =
                !busy && (_formula ? !string.IsNullOrWhiteSpace(_latex.Text) : _table.Document is not null);
            _formulaCopy.IsEnabled = _copy.IsEnabled;
            _formulaOpenWord.IsEnabled = !busy && !_lifetime.IsCancellationRequested && _formula &&
                                         FormulaSourcesForOutput().Length > 0;
            _install.IsEnabled = !busy;
            _tableExcel.IsEnabled = !busy && _table.Document is not null;
            if (_formula) SetFormulaResultsBusy(busy);
        }

        private void OnShortcut(object? sender, KeyEventArgs e)
        {
            if (_busy) return;
            if (e.Handled &&
                !(_formula && _latex.IsFocused && e.Key == Key.C && _latex.SelectedText.Length == 0)) return;
            switch (_formula)
            {
                case true when _latex.IsFocused &&
                               (e.Key is not (Key.C or Key.S) || (e.Key == Key.C && _latex.SelectedText.Length > 0)):
                    return;
                case false when e is { Key: Key.Delete, KeyModifiers: KeyModifiers.None } &&
                                _table is
                                {
                                    IsKeyboardFocusWithin: true, IsEditing: false, HasSelection: true,
                                    Document: { } clearDocument,
                                }:
                    Remember();
                    clearDocument.Clear(_table.SelectedRanges);
                    _table.Refresh();
                    e.Handled = true;
                    return;
            }

            var hotkeys = Application.Current?.PlatformSettings?.HotkeyConfiguration;
            var undo = hotkeys?.Undo.Any(gesture => gesture.Matches(e)) == true;
            var redo = hotkeys?.Redo.Any(gesture => gesture.Matches(e)) == true;
            if (!_formula && _table.Document is { } document && (undo || redo))
            {
                if (!_table.CommitEdit()) return;
                var from = undo ? _undo : _redo;
                var to = undo ? _redo : _undo;
                if (from.TryPop(out var previous))
                {
                    to.Push(new(document.Table, _table.CaptureState()));
                    document.Restore(previous.Table);
                    _table.RestoreState(previous.Grid);
                }

                e.Handled = true;
                return;
            }

            var command = CommandModifier;
            if (!e.KeyModifiers.HasFlag(command)) return;
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (e.Key)
            {
                case Key.C:
                    _operation = OutputAsync(false);
                    e.Handled = true;
                    break;
                case Key.S:
                    _operation = OutputAsync(true);
                    e.Handled = true;
                    break;
            }
        }
    }
}
