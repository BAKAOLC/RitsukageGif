using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel TransferPage(Func<AppSettings> snapshot)
        {
            var page = Page();
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var import = new Button
            {
                Name = "ImportSettings", [!ContentProperty] = LiveText.Resource("SettingsTransferImport"),
            };
            var export = new Button
            {
                Name = "ExportSettings", [!ContentProperty] = LiveText.Resource("SettingsTransferExport"),
            };
            actions.Children.Add(import);
            actions.Children.Add(export);
            page.Children.Add(actions);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            page.Children.Add(status);
            var lifetime = new CancellationTokenSource();
            var token = lifetime.Token;
            RegisterContentCleanup(() =>
            {
                lifetime.Cancel();
                lifetime.Dispose();
            });
            import.Click += async (_, _) =>
            {
                import.IsEnabled = export.IsEnabled = false;
                try
                {
                    var selected = await StorageProvider.OpenFilePickerAsync(new()
                    {
                        Title = UiText.Get("SettingsTransferImport"), AllowMultiple = false,
                        FileTypeFilter = [new("JSON") { Patterns = ["*.json"] }],
                    });
                    if (selected.Count != 1) return;
                    await using var stream = await selected[0].OpenReadAsync();
                    var loaded = await SettingsTransfer.ImportAsync(stream, token);
                    token.ThrowIfCancellationRequested();
                    ReplaceLiveSettings(loaded);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    _error.Text = error.Message;
                }
                finally
                {
                    import.IsEnabled = export.IsEnabled = true;
                }
            };
            export.Click += async (_, _) =>
            {
                import.IsEnabled = export.IsEnabled = false;
                try
                {
                    var values = snapshot();
                    var selected = await StorageProvider.SaveFilePickerAsync(new()
                    {
                        Title = UiText.Get("SettingsTransferExport"),
                        SuggestedFileName = "RitsukageGif-settings.json",
                        DefaultExtension = "json", FileTypeChoices = [new("JSON") { Patterns = ["*.json"] }],
                    });
                    if (selected is null) return;
                    var path = selected.TryGetLocalPath() ??
                               throw new NotSupportedException(UiText.Get("SettingsTransferLocalFileRequired"));
                    await SettingsTransfer.ExportAsync(path, values, token);
                    status.Bind(TextBlock.TextProperty, LiveText.Resource("SettingsTransferExported"));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    _error.Text = error.Message;
                }
                finally
                {
                    import.IsEnabled = export.IsEnabled = true;
                }
            };
            return page;
        }
    }
}
