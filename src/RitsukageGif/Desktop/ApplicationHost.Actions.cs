using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly List<(GlobalActionBinding Action, NativeMenuItem Item)> _actionMenus = [];
        private NativeMenuItemSeparator? _trayActionAnchor;
        private NativeMenuItem? _trayPresets;

        private IEnumerable<string> ReservedActionShortcuts(AppSettings? settings = null)
        {
            settings ??= Settings;
            foreach (var preset in settings.PresetHotkeys) yield return preset.Shortcut;
        }

        private bool CanExecuteAction(GlobalActionBinding action)
        {
            if (IsClosing || !action.Enabled || GlobalActionUnavailableReasonKey(action.Kind) is not null)
                return false;
            return action.Kind switch
            {
                GlobalActionKind.StopRecording => CanStopActiveRecording,
                GlobalActionKind.ToggleRecording => CanToggleRecording,
                GlobalActionKind.StartRecording => !IsRecording && CanToggleRecording,
                GlobalActionKind.ScreenshotAndRecord => CanTakeScreenshot && CanEditRecordingSettings && !IsBusy,
                GlobalActionKind.Screenshot or GlobalActionKind.ScreenshotAndCopy or GlobalActionKind.ScreenshotAndPin
                    or GlobalActionKind.ScreenshotAndSave or GlobalActionKind.ScreenshotQuickSave
                    or GlobalActionKind.ScreenshotAndCopyText
                    or GlobalActionKind.ScreenshotAndCopyCodeText
                    or GlobalActionKind.ScreenshotAndTranslate
                    or GlobalActionKind.ScreenshotAndRecognizeText or GlobalActionKind.ScreenshotAndRecognizeTable
                    or GlobalActionKind.ScreenshotAndRecognizeFormula
                    or GlobalActionKind.ScreenshotAndScroll => CanTakeScreenshot,
                GlobalActionKind.CaptureActiveWindow => WindowLocator.CanFindActiveWindow && CanTakeScreenshot,
                GlobalActionKind.CaptureLastRegion => !DesktopCapture.UsesSourceSelection && CanTakeScreenshot &&
                                                      _lastScreenshotSelection is { Region.IsEmpty: false },
                GlobalActionKind.SelectRegion => CanEditRecordingSettings && !IsSelecting && !IsConfiguring,
                GlobalActionKind.RunScript => !IsSelecting && !IsConfiguring,
                GlobalActionKind.RunPreset => !IsBusy && !IsSelecting && !IsConfiguring,
                GlobalActionKind.HideMainWindow => CanHideToTray && !IsSelecting && !IsConfiguring,
                GlobalActionKind.ShowPinnedImages or GlobalActionKind.HidePinnedImages
                    or GlobalActionKind.ClosePinnedImages or GlobalActionKind.TogglePinnedImages
                    or GlobalActionKind.ToggleOtherPinnedImages =>
                    _pinnedImages.HasImages && !IsSelecting,
                GlobalActionKind.SwitchPinnedGroup => !IsSelecting,
                GlobalActionKind.RestoreLastPinnedImage => _pinnedImages.CanRestoreClosed && !IsSelecting &&
                                                           !IsConfiguring,
                GlobalActionKind.PinClipboard => !IsSelecting && !IsConfiguring,
                GlobalActionKind.PinFile or GlobalActionKind.PinSelectedFiles
                    or GlobalActionKind.ToggleWindowTopmostAtPointer => !IsSelecting && !IsConfiguring,
                GlobalActionKind.TogglePinnedMouseThrough => !IsSelecting && !IsConfiguring &&
                                                             _pinnedImages.CanToggleMouseThrough,
                GlobalActionKind.TranslateClipboard => !IsSelecting && !IsConfiguring,
                GlobalActionKind.ToggleGlobalShortcuts => !IsConfiguring,
                GlobalActionKind.OpenScriptEditor or GlobalActionKind.OpenImageEditor
                    or GlobalActionKind.OpenAnimationEditor
                    or GlobalActionKind.OpenVideoRecorder or
                    GlobalActionKind.OpenOutputDirectory or GlobalActionKind.ShowMainWindow =>
                    !IsSelecting && !IsConfiguring,
                _ => false,
            };
        }

        private static string? GlobalActionUnavailableReasonKey(GlobalActionKind kind)
        {
            return kind switch
            {
                GlobalActionKind.TogglePinnedMouseThrough when !PinnedGroupPointer.IsSupported =>
                    "PinnedMouseThroughUnavailable",
                GlobalActionKind.PinSelectedFiles when !ActiveFileSelection.IsSupported =>
                    "GlobalActionFileSelectionUnavailable",
                GlobalActionKind.ToggleWindowTopmostAtPointer when
                    !ExternalWindowTopmost.IsSupported || !PinnedGroupPointer.IsSupported =>
                    "GlobalActionTopmostUnavailable",
                _ => null,
            };
        }

        private async Task ExecuteActionAsync(GlobalActionBinding action)
        {
            if (!CanExecuteAction(action))
            {
                HostLogger.ZLogDebug(
                    $"Global action rejected by current application state. Kind={action.Kind.ToString()} State={CurrentState}");
                return;
            }

            HostLogger.ZLogInformation($"Global action started. Kind={action.Kind.ToString()} State={CurrentState}");
            try
            {
                await ExecuteActionCoreAsync(action, _hostCancellation.Token);
            }
            catch (OperationCanceledException) when (IsClosing)
            {
                HostLogger.ZLogDebug($"Global action cancelled during shutdown. Kind={action.Kind.ToString()}");
            }
            catch (Exception error)
            {
                HostLogger.ZLogError(error,
                    $"Global action failed. Kind={action.Kind.ToString()} State={CurrentState}");
                if (!IsClosing) SetStatus(error.Message);
            }
        }

        private async Task<ScreenshotInvocation?> ExecuteActionCoreAsync(GlobalActionBinding action,
            CancellationToken token,
            bool propagateScreenshotErrors = false, bool waitForPreset = false)
        {
            switch (action.Kind)
            {
                case GlobalActionKind.SelectRegion:
                    await SelectAsync(token);
                    break;
                case GlobalActionKind.ToggleRecording:
                case GlobalActionKind.StartRecording:
                    await RunRecordingCommandAsync(token);
                    break;
                case GlobalActionKind.StopRecording:
                    await StopActiveRecordingAsync();
                    break;
                case GlobalActionKind.Screenshot:
                case GlobalActionKind.ScreenshotAndCopy:
                case GlobalActionKind.ScreenshotAndPin:
                case GlobalActionKind.ScreenshotAndSave:
                case GlobalActionKind.ScreenshotQuickSave:
                case GlobalActionKind.ScreenshotAndCopyText:
                case GlobalActionKind.ScreenshotAndCopyCodeText:
                case GlobalActionKind.ScreenshotAndTranslate:
                case GlobalActionKind.ScreenshotAndRecognizeText:
                case GlobalActionKind.ScreenshotAndRecognizeTable:
                case GlobalActionKind.ScreenshotAndRecognizeFormula:
                case GlobalActionKind.ScreenshotAndScroll:
                case GlobalActionKind.ScreenshotAndRecord:
                case GlobalActionKind.CaptureActiveWindow:
                case GlobalActionKind.CaptureLastRegion:
                    return await ExecuteScreenshotActionAsync(action, token: token,
                        propagateErrors: propagateScreenshotErrors);
                case GlobalActionKind.TranslateClipboard:
                    await TranslateClipboardAsync(token);
                    break;
                case GlobalActionKind.RunScript:
                    var diagnostic = await RunActionScriptAsync(action, new(), token);
                    if (diagnostic is not null)
                        throw new ScriptExecutionException(diagnostic.Line, diagnostic.Column, diagnostic.Message,
                            sourceFile: diagnostic.Source);
                    break;
                case GlobalActionKind.RunPreset:
                    var presets = PresetCatalog.Load([action.Parameter]);
                    var preset = string.IsNullOrWhiteSpace(action.PresetName)
                        ? presets.Single()
                        : presets.Single(item => item.Name == action.PresetName);
                    var presetParameters = ScriptParameters.Parse(action.Arguments.Select(pair =>
                        pair.Key + "=" + pair.Value));
                    if (IsTaskHost || waitForPreset)
                    {
                        var presetDebugger = new PresetDebugger { DefaultSource = preset.SourceFile };
                        var reply = await RunOwnedPresetCommandAsync(presetDebugger,
                            () => ActivatePreset(preset, presetDebugger, presetParameters), token);
                        if (reply.Diagnostic is { } presetDiagnostic)
                            throw new ScriptExecutionException(presetDiagnostic.Line, presetDiagnostic.Column,
                                presetDiagnostic.Message, sourceFile: presetDiagnostic.Source);
                        if (!reply.Success) throw new InvalidOperationException(reply.Message);
                    }
                    else
                    {
                        ActivatePreset(preset, parameters: presetParameters);
                    }

                    break;
                case GlobalActionKind.OpenScriptEditor:
                    await OpenEditorAsync(string.IsNullOrWhiteSpace(action.Parameter) ? null : action.Parameter, token);
                    break;
                case GlobalActionKind.OpenImageEditor:
                    await ShowImageEditorAsync(string.IsNullOrWhiteSpace(action.Parameter)
                        ? null
                        : action.Parameter, token);
                    break;
                case GlobalActionKind.OpenAnimationEditor:
                    await ShowAnimationEditorAsync(string.IsNullOrWhiteSpace(action.Parameter)
                        ? null
                        : action.Parameter, token);
                    break;
                case GlobalActionKind.OpenVideoRecorder:
                    await OpenVideoRecorderAsync();
                    break;
                case GlobalActionKind.OpenOutputDirectory:
                    PlatformShell.OpenDirectory(LatestOutputPath is { } output
                        ? Path.GetDirectoryName(Path.GetFullPath(output))!
                        : Settings.OutputDirectory);
                    break;
                case GlobalActionKind.ShowMainWindow:
                    ShowMainWindow();
                    break;
                case GlobalActionKind.HideMainWindow:
                    HideToTray();
                    break;
                case GlobalActionKind.ShowPinnedImages:
                    _pinnedImages.ShowAll();
                    break;
                case GlobalActionKind.HidePinnedImages:
                    _pinnedImages.HideAll();
                    break;
                case GlobalActionKind.RestoreLastPinnedImage:
                    await _pinnedImages.RestoreLastClosedAsync(token);
                    break;
                case GlobalActionKind.PinClipboard:
                    await PinActionClipboardAsync(token);
                    break;
                case GlobalActionKind.PinFile:
                    await PinActionFilesAsync([action.Parameter], token);
                    break;
                case GlobalActionKind.PinSelectedFiles:
                    var files = await ActiveFileSelection.ReadAsync(token);
                    if (files.Count == 0) throw new IOException(UiText.Get("GlobalActionNoSelectedFiles"));
                    await PinActionFilesAsync(files, token);
                    break;
                case GlobalActionKind.ToggleWindowTopmostAtPointer:
                    var targetPoint = PinnedGroupPointer.Read() ??
                                      throw new NotSupportedException(UiText.Get("GlobalActionTopmostUnavailable"));
                    var topmost = await ExternalWindowTopmost.ToggleAtPointerAsync(targetPoint, token);
                    SetStatusKey(topmost ? "GlobalActionWindowTopmostEnabled" : "GlobalActionWindowTopmostDisabled");
                    break;
                case GlobalActionKind.TogglePinnedMouseThrough:
                    var point = PinnedGroupPointer.Read() ??
                                throw new NotSupportedException(UiText.Get("PinnedMouseThroughUnavailable"));
                    _pinnedImages.ToggleMouseThroughAtPointer(point);
                    break;
                case GlobalActionKind.ClosePinnedImages:
                    _pinnedImages.CloseAll();
                    break;
                case GlobalActionKind.TogglePinnedImages:
                    _pinnedImages.ToggleAllPinnedVisibility();
                    break;
                case GlobalActionKind.ToggleOtherPinnedImages:
                    if (!_pinnedImages.ToggleOtherPins()) SetStatusKey("PinnedNoSelection");
                    break;
                case GlobalActionKind.SwitchPinnedGroup:
                    _pinnedImages.SwitchPinGroup();
                    break;
                case GlobalActionKind.ToggleGlobalShortcuts:
                    ToggleGlobalShortcuts();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }

            return null;
        }

        private void RebuildActionMenus()
        {
            if (_tray?.Menu is not { } menu) return;
            PopulateActionMenus(menu);
        }

        private void PopulateActionMenus(NativeMenu menu)
        {
            if (_trayActionAnchor is null) return;
            foreach (var (_, item) in _actionMenus) menu.Items.Remove(item);
            _actionMenus.Clear();
            var index = 0;
            foreach (var action in Settings.GlobalActions)
            {
                if (!action.Enabled || !action.ShowInTray) continue;
                var item = new NativeMenuItem(action.Name)
                {
                    Gesture = ParseTrayGesture(action.Shortcut),
                    ToggleType = action.Kind == GlobalActionKind.ToggleGlobalShortcuts
                        ? MenuItemToggleType.CheckBox
                        : MenuItemToggleType.None,
                };
                item.Click += async (_, _) =>
                {
                    if (HasStoppableScreenshotRecording && action.Kind == GlobalActionKind.ToggleRecording)
                        await RunTrayActionAsync(StopActiveRecordingAsync);
                    else await ExecuteActionAsync(action);
                };
                menu.Items.Insert(index++, item);
                _actionMenus.Add((action, item));
            }

            RebuildTrayPresetMenu();
            RefreshActionMenus();
        }

        private void RebuildTrayPresetMenu()
        {
            if (_trayPresets is null) return;
            var menu = new NativeMenu();
            try
            {
                foreach (var preset in PresetCatalog.Load(Settings.PresetFiles))
                {
                    var shortcut = Settings.PresetHotkeys.FirstOrDefault(binding => binding.Preset == preset.Name)
                        ?.Shortcut;
                    var item = new NativeMenuItem(preset.Name)
                    {
                        Gesture = ParseTrayGesture(shortcut ?? ""),
                    };
                    item.Click += async (_, _) => await RunTrayActionAsync(async () =>
                    {
                        var reply = await ExecuteCommandAsync(["preset", "activate", preset.Name]);
                        if (!reply.Success) SetStatus(reply.Message);
                    });
                    menu.Items.Add(item);
                }
            }
            catch (Exception error)
            {
                SetStatus(error.Message);
            }

            _trayPresets.Menu = menu;
        }

        private void RefreshActionMenus()
        {
            if (_trayActionAnchor is null) return;
            if (_trayPresets is not null)
            {
                _trayPresets.Header = UiText.Get("MainPresetsMenu");
                _trayPresets.IsVisible = _trayPresets.Menu?.Items.Count > 0;
                _trayPresets.IsEnabled = !IsBusy && !IsSelecting && !IsConfiguring && !IsClosing;
            }

            if (_trayToggleShortcuts is { } toggle)
            {
                toggle.Header = UiText.Get("TrayNativeDisableShortcuts");
                toggle.IsChecked = _globalShortcutsSuspended;
                toggle.IsEnabled = !IsClosing && !IsConfiguring;
                toggle.IsVisible = true;
            }

            if (_trayDisableFullscreen is { } fullscreen)
            {
                fullscreen.Header = UiText.Get("TrayNativeDisableFullscreen");
                fullscreen.IsChecked = Settings.HotkeyPolicy.DisableInFullscreen;
                fullscreen.IsEnabled = !IsClosing && !IsConfiguring;
                fullscreen.IsVisible = true;
            }

            _trayActionAnchor.IsVisible = _actionMenus.Count > 0;
            foreach (var (action, item) in _actionMenus)
            {
                var stopScreenshot = HasStoppableScreenshotRecording && action.Kind == GlobalActionKind.ToggleRecording;
                item.Header = stopScreenshot ? UiText.Get("GlobalActionStopRecording") : GlobalActions.Label(action);
                item.Gesture = stopScreenshot ? null : ParseTrayGesture(action.Shortcut);
                item.IsVisible = true;
                item.IsEnabled = stopScreenshot ? CanStopActiveRecording : CanExecuteAction(action);
                item.IsChecked = action.Kind == GlobalActionKind.ToggleGlobalShortcuts && _globalShortcutsSuspended;
            }
        }
    }
}
