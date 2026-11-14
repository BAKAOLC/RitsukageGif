using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotSaveDialog : Window
    {
        private string? _path;

        private ScreenshotSaveDialog(IStorageProvider storage, ScreenshotSaveProfile profile, string fallback,
            string titleKey, OutputColorMode mode, ScreenshotNameContext context, bool pngOnly)
        {
            Title = UiText.Get(titleKey);
            Width = 620;
            MinWidth = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            Foreground = AppTheme.Ink;
            Classes.Add("settings-window");
            var directory = new TextBox { Text = profile.ResolveDirectory(fallback, context) };
            var name = new TextBox { Text = Path.GetFileNameWithoutExtension(profile.CreateFileName(context)) };
            var extensions = pngOnly
                ? [".png"]
                : mode switch
                {
                    OutputColorMode.Sdr => new[] { ".png", ".jpg", ".bmp", ".webp", ".avif" },
                    OutputColorMode.Hdr => [".png", ".exr"],
                    OutputColorMode.Both => [".png"],
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                };
            var preferred = profile.DialogExtension(mode);
            if (preferred == ".jpeg") preferred = ".jpg";
            var format = new ComboBox
            {
                ItemsSource = extensions, SelectedIndex = Math.Max(0, Array.IndexOf(extensions, preferred)),
                MinWidth = 100,
            };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted };
            var browse = new Button { Content = UiText.Get("Browse") };
            var save = new Button { Content = UiText.Get("ScreenshotSave"), IsDefault = true };
            var cancel = new Button { Content = UiText.Get("Cancel"), IsCancel = true };
            var page = new StackPanel { Margin = new(20), Spacing = 10 };
            page.Children.Add(new TextBlock { Text = UiText.Get("OutputDirectory") });
            var folderRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            folderRow.Children.Add(directory);
            Grid.SetColumn(browse, 1);
            folderRow.Children.Add(browse);
            page.Children.Add(folderRow);
            page.Children.Add(new TextBlock { Text = UiText.Get("ScreenshotNameTemplate") });
            var nameRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            nameRow.Children.Add(name);
            Grid.SetColumn(format, 1);
            nameRow.Children.Add(format);
            page.Children.Add(nameRow);
            page.Children.Add(status);
            var buttons = new StackPanel
                { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            page.Children.Add(buttons);
            Content = page;

            var closed = false;
            Closed += (_, _) => closed = true;

            name.TextChanged += (_, _) => Update();
            directory.TextChanged += (_, _) => Update();
            format.SelectionChanged += (_, _) => Update();
            cancel.Click += (_, _) => Close();
            save.Click += (_, _) =>
            {
                _path = Update();
                if (_path is not null) Close();
            };
            browse.Click += async (_, _) =>
            {
                IReadOnlyList<IStorageFolder>? folders = null;
                try
                {
                    folders = await storage.OpenFolderPickerAsync(new()
                        { AllowMultiple = false, Title = UiText.Get("OutputDirectory") });
                    if (!closed && folders.Count > 0 && folders[0].TryGetLocalPath() is { } selected)
                        directory.Text = selected;
                }
                catch (Exception error)
                {
                    if (!closed) status.Text = error.Message;
                }
                finally
                {
                    if (folders is not null) StoragePickerAwaiter.DisposeFolders(folders);
                }
            };
            Update();
            return;

            string Destination()
            {
                var stem = name.Text?.Trim() ?? "";
                if (stem.Length == 0 || stem is "." or ".." || stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ArgumentException(UiText.Get("ScreenshotFileNameInvalid"));
                return Path.Combine(Path.GetFullPath(directory.Text ?? ""), stem + extensions[format.SelectedIndex]);
            }

            string? Update()
            {
                try
                {
                    var path = Destination();
                    var exists = mode == OutputColorMode.Both
                        ? Path.Exists(Path.ChangeExtension(path, ".sdr.png")) ||
                          Path.Exists(Path.ChangeExtension(path, ".hdr.png"))
                        : Path.Exists(path);
                    status.Text = exists
                        ? UiText.Get(profile.ConflictPolicy switch
                        {
                            ScreenshotConflictPolicy.Overwrite => "ScreenshotExistingOverwrite",
                            ScreenshotConflictPolicy.Rename => "ScreenshotExistingRename",
                            _ => "ScreenshotExistingFail",
                        })
                        : path;
                    save.IsEnabled = !exists || profile.ConflictPolicy != ScreenshotConflictPolicy.Fail;
                    return save.IsEnabled ? path : null;
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException
                                                  or PathTooLongException)
                {
                    status.Text = error.Message;
                    save.IsEnabled = false;
                    return null;
                }
            }
        }

        internal static async Task<string?> PickAsync(Window? owner, IStorageProvider storage,
            ScreenshotSaveProfile profile, string fallback, string titleKey, OutputColorMode mode,
            ScreenshotNameContext context, bool pngOnly, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var window = new ScreenshotSaveDialog(storage, profile, fallback, titleKey, mode, context, pngOnly);
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
            return window._path;
        }
    }
}
