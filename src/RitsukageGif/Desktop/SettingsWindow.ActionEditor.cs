using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

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
                Name = "GlobalActionEditor", Title = UiText.Get("GlobalActionEdit"), Width = 720, MinWidth = 560,
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
                Name = "ActionName", Text = action.Name, PlaceholderText = UiText.Get("GlobalAction" + action.Kind),
            };
            var kinds = GlobalActions.Catalog.Select(definition => definition.Kind).ToArray();
            var kind = new ComboBox
            {
                Name = "ActionKind", ItemsSource = kinds.Select(item => UiText.Get("GlobalAction" + item)).ToArray(),
                SelectedIndex = Array.IndexOf(kinds, action.Kind),
            };
            var enabled = new CheckBox
                { Name = "ActionEnabled", Content = UiText.Get("GlobalActionEnabled"), IsChecked = action.Enabled };
            var showInTray = new CheckBox
            {
                Name = "ActionShowInTray", Content = UiText.Get("GlobalActionShowInTray"),
                IsChecked = action.ShowInTray,
            };
            var geometry = new ActionScreenshotGeometryEditor(GlobalActions.IsScreenshot(action.Kind) ? action : new());
            using var scriptEditor = new ActionScriptEditor();
            scriptEditor.Name = "ActionInlineEditor";
            scriptEditor.Height = 220;
            var customScript = action.Script;
            GlobalActionKind? previousKind = null;
            CancellationTokenSource? testCancellation = null;
            var testRevision = 0;
            var runtimeOutput = new TextBox
            {
                Name = "ActionScriptOutput", IsReadOnly = true, AcceptsReturn = true,
                MaxHeight = 100, TextWrapping = TextWrapping.Wrap, IsVisible = false,
            };
            var error = new TextBlock
                { Name = "ActionError", Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap };
            scriptEditor.SourceChanged += () =>
            {
                if (token.IsCancellationRequested) return;
                error.Text = "";
                runtimeOutput.Text = "";
                runtimeOutput.IsVisible = false;
            };
            var parameter = new TextBox
            {
                Text = GlobalActions.Definition(action.Kind).FileParameter
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
                try
                {
                    var files = await StoragePickerAwaiter.WaitAsync(dialog.StorageProvider.OpenFilePickerAsync(new()
                    {
                        Title = UiText.Get("GlobalActionFile"), AllowMultiple = false,
                    }), token);
                    try
                    {
                        if (!token.IsCancellationRequested && files.Count > 0)
                            parameter.Text = files[0].TryGetLocalPath();
                    }
                    finally
                    {
                        StoragePickerAwaiter.DisposeFiles(files);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    DisplayError(exception);
                }
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
                    ActionScreenshotOutput.CopyCodeText => "CaptureCodeCopyText",
                    ActionScreenshotOutput.Translate => "GlobalActionScreenshotAndTranslate",
                    ActionScreenshotOutput.RecognizeText => "RecognizeText",
                    ActionScreenshotOutput.RecognizeTable => "TableRecognition",
                    ActionScreenshotOutput.RecognizeFormula => "FormulaRecognition",
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
                PlaceholderText = UiText.Get("GlobalActionCurrentOutputDirectory"),
            };
            var browseDirectory = new Button { Content = UiText.Get("Browse") };
            var directoryInput = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            directoryInput.Children.Add(directory);
            Grid.SetColumn(browseDirectory, 1);
            directoryInput.Children.Add(browseDirectory);
            var directoryRow = Labeled("OutputDirectory", directoryInput);
            browseDirectory.Click += async (_, _) =>
            {
                try
                {
                    var folders = await StoragePickerAwaiter.WaitAsync(dialog.StorageProvider.OpenFolderPickerAsync(
                        new()
                        {
                            Title = UiText.Get("OutputDirectory"), AllowMultiple = false,
                        }), token);
                    try
                    {
                        if (!token.IsCancellationRequested && folders.Count > 0)
                            directory.Text = folders[0].TryGetLocalPath();
                    }
                    finally
                    {
                        StoragePickerAwaiter.DisposeFolders(folders);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    DisplayError(exception);
                }
            };
            var preset = new TextBox
            {
                Text = action.PresetName, PlaceholderText = UiText.Get("GlobalActionPresetNameHint"),
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

            var ignoredPrograms = action.IgnoredPrograms.ToList();
            var ignoredButton = new Button
            {
                Content = string.Format(UiText.Get("HotkeyIgnoredPrograms"), ignoredPrograms.Count),
                HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = PortableForegroundPolicy.Supported,
            };
            if (!PortableForegroundPolicy.Supported)
                ignoredButton.Bind(ToolTip.TipProperty, LiveText.Resource("HotkeyPolicyUnavailable"));
            ignoredButton.Click += async (_, _) =>
            {
                var edited = await EditIgnoredProgramsAsync(ignoredPrograms, dialog);
                if (edited is null) return;
                ignoredPrograms = edited;
                ignoredButton.Content = string.Format(UiText.Get("HotkeyIgnoredPrograms"), ignoredPrograms.Count);
            };
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(Labeled("GlobalActionName", name));
            content.Children.Add(Labeled("GlobalActionKind", kind));
            content.Children.Add(new StackPanel
                { Orientation = Orientation.Horizontal, Spacing = 12, Children = { enabled, showInTray } });
            content.Children.Add(ignoredButton);
            content.Children.Add(sourceRow);
            content.Children.Add(delayRow);
            content.Children.Add(geometry);
            content.Children.Add(outputRow);
            content.Children.Add(directoryRow);
            content.Children.Add(fileRow);
            content.Children.Add(presetRow);
            content.Children.Add(argumentGroup);
            content.Children.Add(scriptEditor);
            content.Children.Add(runtimeOutput);
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
            var test = new Button
            {
                Name = "ActionScriptTest", Content = UiText.Get("ActionScriptTest"),
                IsEnabled = RunActionScript is not null,
            };
            var stop = new Button
                { Name = "ActionScriptStop", Content = UiText.Get("ActionScriptStop"), IsEnabled = false };
            buttons.Children.Add(test);
            buttons.Children.Add(stop);
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
            addArgument.Click += (_, _) => AddArgument("", "");
            if (action.Kind == GlobalActionKind.RunPreset)
                foreach (var pair in action.Arguments)
                    AddArgument(pair.Key, pair.Value);

            kind.SelectionChanged += (_, _) =>
            {
                var selected = kind.SelectedIndex >= 0 ? kinds[kind.SelectedIndex] : action.Kind;
                var label = UiText.Get("GlobalAction" + selected);
                name.PlaceholderText = label;
                ShowFields();
            };
            output.SelectionChanged += (_, _) => ShowFields();
            ShowFields();
            cancel.Click += (_, _) => dialog.Close(false);
            save.Click += (_, _) =>
            {
                try
                {
                    var draft = ReadDraft();
                    action.Name = draft.Name;
                    action.Kind = draft.Kind;
                    action.Enabled = draft.Enabled;
                    action.ShowInTray = draft.ShowInTray;
                    action.IgnoredPrograms = draft.IgnoredPrograms;
                    action.Parameter = draft.Parameter;
                    action.PresetName = draft.PresetName;
                    action.Arguments = draft.Arguments;
                    action.Script = draft.Script;
                    dialog.Close(true);
                }
                catch (Exception exception) when (exception is ArgumentException or PresetSyntaxException or IOException
                                                      or NotSupportedException)
                {
                    DisplayError(exception);
                }
            };
            test.Click += async (_, _) =>
            {
                if (RunActionScript is null || testCancellation is not null) return;
                // ReSharper disable once AccessToModifiedClosure
                var revision = ++testRevision;
                try
                {
                    var draft = ReadDraft();
                    var sourceText = ScriptSource(draft);
                    _ = ActionScriptCompilation.Compile(sourceText);
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                    testCancellation = cancellation;
                    test.IsEnabled = save.IsEnabled = false;
                    stop.IsEnabled = true;
                    content.IsEnabled = false;
                    runtimeOutput.Text = "";
                    runtimeOutput.IsVisible = true;
                    error.Text = "";
                    var debugger = new PresetDebugger();

                    var outputPending = 0;

                    void Changed()
                    {
                        if (Interlocked.Exchange(ref outputPending, 1) != 0) return;
                        Dispatcher.UIThread.Post(() =>
                        {
                            Volatile.Write(ref outputPending, 0);
                            if (revision != testRevision || token.IsCancellationRequested) return;
                            var snapshot = debugger.Snapshot;
                            runtimeOutput.Text = string.Join(Environment.NewLine, snapshot.Output);
                        });
                    }

                    debugger.Changed += Changed;
                    try
                    {
                        var diagnostic = await RunActionScript(draft with
                        {
                            Kind = GlobalActionKind.RunScript, Script = sourceText, Parameter = "",
                            PresetName = "", Arguments = new(StringComparer.Ordinal),
                        }, debugger, cancellation.Token);
                        if (!token.IsCancellationRequested && revision == testRevision)
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            scriptEditor.ShowDiagnostic(diagnostic);
                            error.Foreground = diagnostic is null ? AppTheme.Muted : AppTheme.Error;
                            error.Text = diagnostic is null
                                ? UiText.Get("ActionScriptTestFinished")
                                : ActionScriptCompilation.FormatDiagnostic(diagnostic);
                            runtimeOutput.Text = string.Join(Environment.NewLine, debugger.Snapshot.Output);
                        }
                    }
                    finally
                    {
                        debugger.Changed -= Changed;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested ||
                                                         testCancellation?.IsCancellationRequested == true)
                {
                    if (!token.IsCancellationRequested)
                    {
                        error.Foreground = AppTheme.Muted;
                        error.Text = UiText.Get("ActionScriptTestCancelled");
                    }
                }
                catch (Exception exception)
                {
                    if (!token.IsCancellationRequested) DisplayError(exception);
                }
                finally
                {
                    testCancellation = null;
                    if (!token.IsCancellationRequested)
                    {
                        test.IsEnabled = RunActionScript is not null;
                        save.IsEnabled = true;
                        stop.IsEnabled = false;
                        content.IsEnabled = true;
                    }
                }
            };
            stop.Click += (_, _) => testCancellation?.Cancel();
            foreach (var input in new[] { parameter, delay, directory, preset })
                input.TextChanged += (_, _) => RefreshScript();
            source.SelectionChanged += (_, _) => RefreshScript();
            geometry.AddHandler(TextBox.TextChangedEvent, (_, _) => RefreshScript());
            geometry.AddHandler(ToggleButton.IsCheckedChangedEvent, (_, _) => RefreshScript());
            try
            {
                return await dialog.ShowDialog<bool>(this);
            }
            finally
            {
                dialog.Closed -= onClosed;
                ++testRevision;
                if (testCancellation is { } pendingTest) await pendingTest.CancelAsync();
            }

            void ShowFields()
            {
                if (token.IsCancellationRequested) return;
                var selected = kind.SelectedIndex >= 0 ? kinds[kind.SelectedIndex] : action.Kind;
                sourceRow.IsVisible = GlobalActions.IsScreenshot(selected);
                delayRow.IsVisible = sourceRow.IsVisible;
                outputRow.IsVisible = GlobalActions.IsDirectScreenshot(selected);
                directoryRow.IsVisible = selected == GlobalActionKind.ScreenshotQuickSave || (outputRow.IsVisible &&
                    outputKinds[Math.Clamp(output.SelectedIndex, 0, outputKinds.Length - 1)] ==
                    ActionScreenshotOutput.QuickSave);
                fileRow.IsVisible = GlobalActions.Definition(selected).FileParameter;
                presetRow.IsVisible = argumentGroup.IsVisible = selected == GlobalActionKind.RunPreset;
                geometry.IsVisible = sourceRow.IsVisible && !GlobalActions.IsDirectScreenshot(selected);
                if (previousKind != selected)
                {
                    // ReSharper disable once AccessToDisposedClosure
                    if (previousKind == GlobalActionKind.RunScript) customScript = scriptEditor.Text;
                    previousKind = selected;
                    // ReSharper disable once AccessToDisposedClosure
                    scriptEditor.IsReadOnly = selected != GlobalActionKind.RunScript;
                    // ReSharper disable once AccessToDisposedClosure
                    if (selected == GlobalActionKind.RunScript) scriptEditor.Text = customScript;
                }

                RefreshScript();
            }

            GlobalActionBinding ReadDraft()
            {
                var selected = kind.SelectedIndex >= 0 ? kinds[kind.SelectedIndex] : action.Kind;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (selected == GlobalActionKind.RunPreset)
                    // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                    foreach (var pair in argumentEditors)
                    {
                        var key = pair.Name.Text?.Trim() ?? "";
                        var value = pair.Value.Text ?? "";
                        if (key.Length == 0 && value.Length == 0) continue;
                        if (key.Length == 0 || key.Contains('=') || !values.TryAdd(key, value))
                            throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
                    }

                if (GlobalActions.IsScreenshot(selected))
                {
                    values["delay"] = delay.Text?.Trim() ?? "";
                    var parsedDelay = GlobalActions.ScreenshotDelay(new() { Arguments = values });
                    if (parsedDelay == TimeSpan.Zero) values.Remove("delay");
                    else values["delay"] = parsedDelay.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                    if (GlobalActions.IsDirectScreenshot(selected))
                        values["output"] = outputKinds[Math.Clamp(output.SelectedIndex, 0, outputKinds.Length - 1)]
                            .ToString();
                    if (directoryRow.IsVisible && !string.IsNullOrWhiteSpace(directory.Text))
                        values["directory"] = Path.GetFullPath(directory.Text.Trim());
                    if (geometry.IsVisible) geometry.WriteArguments(values);
                }

                var draft = action with
                {
                    Name = name.Text?.Trim() ?? "", Kind = selected, Enabled = enabled.IsChecked == true,
                    ShowInTray = showInTray.IsChecked == true, IgnoredPrograms = [.. ignoredPrograms],
                    Parameter = GlobalActions.IsScreenshot(selected)
                        ? source.SelectedIndex switch { 1 => "screen", 2 => "window", _ => "" }
                        : fileRow.IsVisible
                            ? parameter.Text?.Trim() ?? ""
                            : "",
                    PresetName = selected == GlobalActionKind.RunPreset ? preset.Text?.Trim() ?? "" : "",
                    // ReSharper disable once AccessToDisposedClosure
                    Arguments = values, Script = selected == GlobalActionKind.RunScript ? scriptEditor.Text : "",
                };
                if (selected == GlobalActionKind.RunScript) _ = ActionScriptCompilation.Compile(draft.Script);
                var errors = GlobalActions.Validate([draft with { Enabled = true }], []);
                if (errors.Count != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
                return draft;
            }

            string ScriptSource(GlobalActionBinding draft)
            {
                if (draft.Kind == GlobalActionKind.RunScript) return draft.Script;
                var builder = new StringBuilder("return actions.run(ActionKind.").Append(draft.Kind)
                    .Append(", {parameter:")
                    .Append(Quote(draft.Parameter)).Append(",preset:").Append(Quote(draft.PresetName))
                    .Append(",arguments:{");
                var first = true;
                foreach (var (key, value) in draft.Arguments)
                {
                    if (!first) builder.Append(',');
                    first = false;
                    builder.Append(Quote(key)).Append(':').Append(Quote(value));
                }

                return builder.Append("}});").ToString();
            }

            static string Quote(string value)
            {
                return "\"" + JsonEncodedText.Encode(value) + "\"";
            }

            void RefreshScript()
            {
                if (token.IsCancellationRequested) return;
                // ReSharper disable once AccessToDisposedClosure
                if (scriptEditor.IsReadOnly)
                    try
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        scriptEditor.Text = ScriptSource(ReadDraft());
                        error.Text = "";
                    }
                    catch (Exception exception) when (exception is ArgumentException or IOException
                                                          or NotSupportedException)
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        scriptEditor.Text = "";
                        error.Foreground = AppTheme.Error;
                        error.Text = exception.Message;
                    }
            }

            void DisplayError(Exception exception)
            {
                if (token.IsCancellationRequested) return;
                var diagnostic = ActionScriptCompilation.Diagnostic(exception);
                error.Foreground = AppTheme.Error;
                error.Text = ActionScriptCompilation.FormatDiagnostic(diagnostic);
                // ReSharper disable once AccessToDisposedClosure
                scriptEditor.ShowDiagnostic(diagnostic);
            }

            void AddArgument(string key, string value)
            {
                var keyBox = new TextBox { Text = key, PlaceholderText = UiText.Get("GlobalActionArgumentName") };
                var valueBox = new TextBox { Text = value, PlaceholderText = UiText.Get("GlobalActionArgumentValue") };
                var remove = BindingButton("SelectionShortcutRemove",
                    ToolbarIcon.GetDefinition("SelectionShortcutRemove"));
                var row = new Grid { ColumnDefinitions = new("*,*,28"), ColumnSpacing = 6 };
                row.Children.Add(keyBox);
                Grid.SetColumn(valueBox, 1);
                row.Children.Add(valueBox);
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                keyBox.TextChanged += (_, _) => RefreshScript();
                valueBox.TextChanged += (_, _) => RefreshScript();
                argumentEditors.Add((keyBox, valueBox));
                arguments.Children.Add(row);
                remove.Click += (_, _) =>
                {
                    argumentEditors.Remove((keyBox, valueBox));
                    arguments.Children.Remove(row);
                    RefreshScript();
                };
            }
        }
    }
}
