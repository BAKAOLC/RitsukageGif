using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class DocumentExportWindow : Window
    {
        private string? _output;
        private bool _saving;

        private DocumentExportWindow(byte[] image, string suggestedName, string? directory, CancellationToken token)
        {
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            var cancellation = lifetime.Token;
            Title = UiText.Get("DocumentExport");
            Width = 440;
            MinWidth = 400;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Classes.Add("settings-window");
            var paper = Choice("DocumentOriginalSize", "A4", "A3", "Letter", "DocumentCustomSize");
            var orientation = Choice("DocumentAutomatic", "DocumentPortrait", "DocumentLandscape");
            var paging = Choice("DocumentFitPage", "DocumentPaginate");
            var direction = Choice("DocumentVertical", "DocumentHorizontal");
            var width = Number(210, 1, 5080);
            var height = Number(297, 1, 5080);
            var margin = Number(0, 0, 1000);
            var dpi = Number(96, 1, 2400);
            var quality = Number(90, 0, 100);
            var lossless = new CheckBox { Content = UiText.Get("DocumentLossless"), IsChecked = true };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            var save = new Button { Content = UiText.Get("DocumentExport"), IsDefault = true };
            var cancel = new Button { Content = UiText.Get("Cancel"), IsCancel = true };
            var form = new Grid { ColumnDefinitions = new("*,190"), RowSpacing = 6, ColumnSpacing = 12 };
            Add("DocumentPaperSize", paper);
            Add("DocumentWidth", width);
            Add("DocumentHeight", height);
            Add("DocumentOrientation", orientation);
            Add("DocumentPageMode", paging);
            Add("DocumentPaginationDirection", direction);
            Add("DocumentMargins", margin);
            Add("DocumentDpi", dpi);
            Add("ScreenshotSaveQuality", quality);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
                Children = { save, cancel },
            };
            Content = new StackPanel { Margin = new(16), Spacing = 10, Children = { form, lossless, status, actions } };
            paper.SelectionChanged += (_, _) => Update();
            paging.SelectionChanged += (_, _) => Update();
            lossless.IsCheckedChanged += (_, _) => Update();
            cancel.Click += (_, _) =>
            {
                lifetime.Cancel();
                if (!_saving) Close();
            };
            Closing += (_, args) =>
            {
                if (_saving)
                {
                    args.Cancel = true;
                    lifetime.Cancel();
                }
            };
            Closed += (_, _) =>
            {
                lifetime.Cancel();
                lifetime.Dispose();
            };
            save.Click += async (_, _) =>
            {
                _saving = true;
                status.IsVisible = false;
                save.IsEnabled = false;
                try
                {
                    var options = new DocumentOutputOptions
                    {
                        PaperSize = (DocumentPaperSize)paper.SelectedIndex,
                        Orientation = (DocumentOrientation)orientation.SelectedIndex,
                        PageMode = (DocumentPageMode)paging.SelectedIndex,
                        PaginationDirection = (DocumentPaginationDirection)direction.SelectedIndex,
                        WidthMillimeters = (double)(width.Value ?? 210),
                        HeightMillimeters = (double)(height.Value ?? 297),
                        Margins = new()
                        {
                            Left = (double)(margin.Value ?? 0), Top = (double)(margin.Value ?? 0),
                            Right = (double)(margin.Value ?? 0), Bottom = (double)(margin.Value ?? 0),
                        },
                        Dpi = (double)(dpi.Value ?? 96), Quality = (int)(quality.Value ?? 90),
                        Lossless = lossless.IsChecked is true, Overwrite = true,
                    };
                    using var location = directory is null
                        ? null
                        : await StoragePickerAwaiter.WaitAsync(
                            StorageProvider.TryGetFolderFromPathAsync(directory), cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    using var selected = await StoragePickerAwaiter.WaitAsync(StorageProvider.SaveFilePickerAsync(new()
                    {
                        Title = Title!, SuggestedFileName = Path.ChangeExtension(suggestedName, ".pdf"),
                        DefaultExtension = "pdf",
                        FileTypeChoices = [new("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] }],
                        ShowOverwritePrompt = true,
                        SuggestedStartLocation = location,
                    }), cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    if (selected?.TryGetLocalPath() is not { } path) return;
                    status.Text = UiText.Get("DocumentExporting");
                    status.IsVisible = true;
                    var result = await new PdfDocumentService().SaveImageAsync(image, path, options, cancellation);
                    _output = result.Path;
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    status.Text = error.Message;
                    status.IsVisible = true;
                }
                finally
                {
                    _saving = false;
                    save.IsEnabled = true;
                    if (_output is not null || lifetime.IsCancellationRequested) Close();
                }
            };
            Update();
            return;

            void Add(string key, Control control)
            {
                var row = form.RowDefinitions.Count;
                form.RowDefinitions.Add(new(GridLength.Auto));
                var label = new TextBlock { Text = UiText.Get(key), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, row);
                Grid.SetRow(control, row);
                Grid.SetColumn(control, 1);
                form.Children.Add(label);
                form.Children.Add(control);
            }

            void Update()
            {
                width.IsEnabled = height.IsEnabled = paper.SelectedIndex == (int)DocumentPaperSize.Custom;
                orientation.IsEnabled = paging.IsEnabled = paper.SelectedIndex != (int)DocumentPaperSize.Original;
                direction.IsEnabled = paging is { IsEnabled: true, SelectedIndex: (int)DocumentPageMode.Paginate };
                quality.IsEnabled = lossless.IsChecked is not true;
            }
        }

        internal static async Task<string?> ShowAsync(Window? owner, byte[] image, string suggestedName,
            CancellationToken token, string? directory = null)
        {
            token.ThrowIfCancellationRequested();
            var window = new DocumentExportWindow(image, suggestedName, directory, token);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => completion.TrySetResult();
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            if (owner is { IsVisible: true })
            {
                await window.ShowDialog(owner);
            }
            else
            {
                window.Show();
                await completion.Task;
            }

            token.ThrowIfCancellationRequested();
            return window._output;
        }

        private static ComboBox Choice(params string[] keys)
        {
            return new()
            {
                ItemsSource = keys.Select(key => key is "A4" or "A3" or "Letter" ? key : UiText.Get(key)).ToArray(),
                SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
        }

        private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum)
        {
            return new()
            {
                Value = value, Minimum = minimum, Maximum = maximum, FormatString = "0.##",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
        }
    }
}
