using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private async Task<bool> EditGlobalActionAsync(GlobalActionBinding action)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ContentCancellationToken);
            var token = lifetime.Token;
            var dialog = new Window
            {
                Name = "GlobalActionEditor", Title = UiText.Get("GlobalActionEdit"), Width = 540, MinWidth = 460,
                MinHeight = 280, MaxHeight = 720,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            };
            // ReSharper disable once AccessToDisposedClosure
            EventHandler onClosed = (_, _) => lifetime.Cancel();
            dialog.Closed += onClosed;
            dialog.Classes.Add("settings-window");
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                var maximumHeight = Math.Max(1, Math.Min(720, screen.WorkingArea.Height / screen.Scaling - 32));
                dialog.MinHeight = Math.Min(280, maximumHeight);
                dialog.MaxHeight = maximumHeight;
            }

            var name = new TextBox
            {
                Name = "ActionName", Text = action.Name, Watermark = UiText.Get("GlobalAction" + action.Kind),
            };
            var kinds = Enum.GetValues<GlobalActionKind>();
            var kind = new ComboBox
            {
                Name = "ActionKind", ItemsSource = kinds.Select(item => UiText.Get("GlobalAction" + item)).ToArray(),
                SelectedIndex = Array.IndexOf(kinds, action.Kind),
            };
            var enabled = new CheckBox
                { Name = "ActionEnabled", Content = UiText.Get("GlobalActionEnabled"), IsChecked = action.Enabled };
            var parameter = new TextBox
            {
                Text = action.Kind is GlobalActionKind.RunPreset or GlobalActionKind.OpenScriptEditor
                    or GlobalActionKind.OpenImageEditor
                    ? action.Parameter
                    : "",
            };
            var browse = new Button { Content = UiText.Get("Browse") };
            var fileInput = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            fileInput.Children.Add(parameter);
            Grid.SetColumn(browse, 1);
            fileInput.Children.Add(browse);
            var fileRow = Labeled("GlobalActionFile", fileInput);
            browse.Click += async (_, _) =>
            {
                var files = await dialog.StorageProvider.OpenFilePickerAsync(new()
                {
                    Title = UiText.Get("GlobalActionFile"), AllowMultiple = false,
                });
                if (files.Count > 0) parameter.Text = files[0].TryGetLocalPath();
            };
            var source = new ComboBox
            {
                Name = "ActionSource", ItemsSource = new[]
                    { UiText.Get("GlobalActionCurrentSource"), UiText.Get("SourceScreen"), UiText.Get("SourceWindow") },
                SelectedIndex = action.Parameter switch { "screen" => 1, "window" => 2, _ => 0 },
            };
            var sourceRow = Labeled("CaptureSource", source);
            var delay = new TextBox
            {
                Name = "ActionDelay", Text = GlobalActions.IsScreenshot(action.Kind)
                    ? action.Arguments.GetValueOrDefault("delay") ?? "0"
                    : "0",
            };
            var delayRow = Labeled("GlobalActionDelay", delay);
            var outputKinds = Enum.GetValues<ActionScreenshotOutput>();
            var output = new ComboBox
            {
                Name = "ActionOutput", ItemsSource = outputKinds.Select(item => UiText.Get(item switch
                {
                    ActionScreenshotOutput.Copy => "ScreenshotCopy",
                    ActionScreenshotOutput.Pin => "ScreenshotPin",
                    ActionScreenshotOutput.Save => "ScreenshotSave",
                    ActionScreenshotOutput.QuickSave => "ScreenshotQuickSave",
                    ActionScreenshotOutput.CopyText => "GlobalActionScreenshotAndCopyText",
                    _ => throw new ArgumentOutOfRangeException(nameof(item)),
                })).ToArray(),
                SelectedIndex = 0,
            };
            try
            {
                output.SelectedIndex = Array.IndexOf(outputKinds, GlobalActions.ScreenshotOutput(action));
            }
            catch (ArgumentException)
            {
                output.SelectedIndex = 0;
            }

            var outputRow = Labeled("GlobalActionCaptureOutput", output);
            var directory = new TextBox
            {
                Name = "ActionDirectory",
                Text = GlobalActions.IsScreenshot(action.Kind)
                    ? action.Arguments.GetValueOrDefault("directory") ?? ""
                    : "",
                Watermark = UiText.Get("GlobalActionCurrentOutputDirectory"),
            };
            var browseDirectory = new Button { Content = UiText.Get("Browse") };
            var directoryInput = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            directoryInput.Children.Add(directory);
            Grid.SetColumn(browseDirectory, 1);
            directoryInput.Children.Add(browseDirectory);
            var directoryRow = Labeled("OutputDirectory", directoryInput);
            browseDirectory.Click += async (_, _) =>
            {
                var folders = await dialog.StorageProvider.OpenFolderPickerAsync(new()
                {
                    Title = UiText.Get("OutputDirectory"), AllowMultiple = false,
                });
                if (folders.Count > 0) directory.Text = folders[0].TryGetLocalPath();
            };
            var preset = new TextBox
            {
                Text = action.PresetName, Watermark = UiText.Get("GlobalActionPresetNameHint"),
            };
            var presetChoices = new ComboBox
            {
                PlaceholderText = UiText.Get("GlobalActionSelectPreset"), MinWidth = 150,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var presetInput = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            presetInput.Children.Add(preset);
            Grid.SetColumn(presetChoices, 1);
            presetInput.Children.Add(presetChoices);
            var presetRow = Labeled("GlobalActionPresetName", presetInput);
            presetChoices.SelectionChanged += (_, _) =>
            {
                if (presetChoices.SelectedItem is string selected) preset.Text = selected;
            };
            var arguments = new StackPanel { Spacing = 6 };
            var argumentEditors = new List<(TextBox Name, TextBox Value)>();
            var argumentGroup = new StackPanel { Spacing = 8 };
            argumentGroup.Children.Add(new TextBlock { Text = UiText.Get("GlobalActionArguments") });
            argumentGroup.Children.Add(arguments);
            var addArgument = new Button
            {
                Content = UiText.Get("GlobalActionAddArgument"), HorizontalAlignment = HorizontalAlignment.Left,
            };
            argumentGroup.Children.Add(addArgument);
            addArgument.Click += (_, _) => AddArgument("", "");
            if (action.Kind == GlobalActionKind.RunPreset)
                foreach (var pair in action.Arguments)
                    AddArgument(pair.Key, pair.Value);
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(Labeled("GlobalActionName", name));
            content.Children.Add(Labeled("GlobalActionKind", kind));
            content.Children.Add(enabled);
            content.Children.Add(sourceRow);
            content.Children.Add(delayRow);
            content.Children.Add(outputRow);
            content.Children.Add(directoryRow);
            content.Children.Add(fileRow);
            content.Children.Add(presetRow);
            content.Children.Add(argumentGroup);
            var error = new TextBlock
                { Name = "ActionError", Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap };
            presetChoices.DropDownOpened += async (_, _) =>
            {
                var path = parameter.Text?.Trim();
                if (string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    var names = await Task.Run(() => PresetCatalog.Load([path]).Select(item => item.Name).ToArray(),
                        token);
                    token.ThrowIfCancellationRequested();
                    if (parameter.Text?.Trim() != path || !dialog.IsVisible) return;
                    presetChoices.ItemsSource = names;
                    error.Text = "";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                      or ArgumentException or FormatException)
                {
                    if (!token.IsCancellationRequested && dialog.IsVisible) error.Text = exception.Message;
                }
            };
            parameter.TextChanged += (_, _) => presetChoices.ItemsSource = null;
            var save = new Button { Name = "ActionApply", Content = UiText.Get("GlobalActionApply") };
            var cancel = new Button { Name = "ActionCancel", Content = UiText.Get("Cancel") };
            var buttons = new StackPanel
                { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            var footer = new StackPanel { Spacing = 8 };
            footer.Children.Add(error);
            footer.Children.Add(buttons);
            var layout = new Grid { RowDefinitions = new("*,Auto"), Margin = new(16), RowSpacing = 12 };
            layout.Children.Add(new ScrollViewer
            {
                Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            Grid.SetRow(footer, 1);
            layout.Children.Add(footer);
            dialog.Content = layout;
            kind.SelectionChanged += (_, _) =>
            {
                var selected = kind.SelectedIndex >= 0 ? kinds[kind.SelectedIndex] : action.Kind;
                var label = UiText.Get("GlobalAction" + selected);
                name.Watermark = label;
                ShowFields();
            };
            output.SelectionChanged += (_, _) => ShowFields();
            ShowFields();
            cancel.Click += (_, _) => dialog.Close(false);
            save.Click += (_, _) =>
            {
                var selectedKind = kind.SelectedIndex >= 0 ? kinds[kind.SelectedIndex] : action.Kind;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (selectedKind == GlobalActionKind.RunPreset)
                    foreach (var (key, value) in argumentEditors
                                 .Select(pair => (Key: pair.Name.Text?.Trim() ?? "", Value: pair.Value.Text ?? ""))
                                 .Where(pair => pair.Key.Length != 0 || pair.Value.Length != 0))
                        if (key.Length == 0 || key.Contains('=') || !values.TryAdd(key, value))
                        {
                            error.Text = UiText.Get("ScriptParametersInvalid");
                            return;
                        }

                if (selectedKind == GlobalActionKind.RunPreset)
                {
                    if (string.IsNullOrWhiteSpace(parameter.Text))
                    {
                        error.Text = UiText.Get("GlobalActionFileRequired");
                        return;
                    }

                    try
                    {
                        _ = ScriptParameters.Parse(values.Select(pair => pair.Key + "=" + pair.Value));
                    }
                    catch (ArgumentException exception)
                    {
                        error.Text = exception.Message;
                        return;
                    }
                }

                var captureArguments = new Dictionary<string, string>(StringComparer.Ordinal);
                if (delayRow.IsVisible)
                {
                    captureArguments["delay"] = delay.Text?.Trim() ?? "";
                    try
                    {
                        _ = GlobalActions.ScreenshotDelay(new() { Arguments = captureArguments });
                    }
                    catch (ArgumentException exception)
                    {
                        error.Text = exception.Message;
                        return;
                    }

                    captureArguments["delay"] = double.Parse(captureArguments["delay"],
                        CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    if (captureArguments["delay"] == "0") captureArguments.Remove("delay");
                }

                if (GlobalActions.IsDirectScreenshot(selectedKind))
                    captureArguments["output"] =
                        outputKinds[Math.Clamp(output.SelectedIndex, 0, outputKinds.Length - 1)].ToString();
                if (directoryRow.IsVisible && !string.IsNullOrWhiteSpace(directory.Text))
                    try
                    {
                        captureArguments["directory"] = Path.GetFullPath(directory.Text.Trim());
                    }
                    catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                                                          or PathTooLongException)
                    {
                        error.Text = exception.Message;
                        return;
                    }

                action.Name = name.Text?.Trim() ?? "";
                action.Kind = selectedKind;
                action.Enabled = enabled.IsChecked == true;
                action.Parameter = GlobalActions.IsScreenshot(selectedKind)
                    ? source.SelectedIndex switch { 1 => "screen", 2 => "window", _ => "" }
                    : fileRow.IsVisible
                        ? parameter.Text?.Trim() ?? ""
                        : "";
                action.PresetName = selectedKind == GlobalActionKind.RunPreset ? preset.Text?.Trim() ?? "" : "";
                action.Arguments = selectedKind == GlobalActionKind.RunPreset ? values : captureArguments;
                dialog.Close(true);
            };
            try
            {
                return await dialog.ShowDialog<bool>(this);
            }
            finally
            {
                dialog.Closed -= onClosed;
            }

            void ShowFields()
            {
                var selected = kind.SelectedIndex >= 0 ? kinds[kind.SelectedIndex] : action.Kind;
                sourceRow.IsVisible = GlobalActions.IsScreenshot(selected);
                delayRow.IsVisible = sourceRow.IsVisible;
                outputRow.IsVisible = GlobalActions.IsDirectScreenshot(selected);
                directoryRow.IsVisible = selected == GlobalActionKind.ScreenshotQuickSave || (outputRow.IsVisible &&
                    outputKinds[Math.Clamp(output.SelectedIndex, 0, outputKinds.Length - 1)] ==
                    ActionScreenshotOutput.QuickSave);
                fileRow.IsVisible = selected is GlobalActionKind.RunPreset or GlobalActionKind.OpenScriptEditor
                    or GlobalActionKind.OpenImageEditor;
                presetRow.IsVisible = argumentGroup.IsVisible = selected == GlobalActionKind.RunPreset;
            }

            void AddArgument(string key, string value)
            {
                var keyBox = new TextBox { Text = key, Watermark = UiText.Get("GlobalActionArgumentName") };
                var valueBox = new TextBox { Text = value, Watermark = UiText.Get("GlobalActionArgumentValue") };
                var remove = BindingButton("SelectionShortcutRemove", "M6,6 L18,18 M18,6 L6,18");
                var row = new Grid { ColumnDefinitions = new("*,*,28"), ColumnSpacing = 6 };
                row.Children.Add(keyBox);
                Grid.SetColumn(valueBox, 1);
                row.Children.Add(valueBox);
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                argumentEditors.Add((keyBox, valueBox));
                arguments.Children.Add(row);
                remove.Click += (_, _) =>
                {
                    argumentEditors.Remove((keyBox, valueBox));
                    arguments.Children.Remove(row);
                };
            }
        }
    }
}
