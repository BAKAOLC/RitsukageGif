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
            if (IsClosing || !action.Enabled) return false;
            return action.Kind switch
            {
                GlobalActionKind.StopRecording => IsRecording,
                GlobalActionKind.ToggleRecording => CanToggleRecording,
                GlobalActionKind.StartRecording => !IsRecording && CanToggleRecording,
                GlobalActionKind.Screenshot or GlobalActionKind.ScreenshotAndCopy or GlobalActionKind.ScreenshotAndPin
                    or GlobalActionKind.ScreenshotAndSave or GlobalActionKind.ScreenshotQuickSave
                    or GlobalActionKind.ScreenshotAndCopyText
                    or GlobalActionKind.ScreenshotAndScroll => CanTakeScreenshot,
                GlobalActionKind.CaptureActiveWindow => OperatingSystem.IsWindows() && CanTakeScreenshot,
                GlobalActionKind.CaptureLastRegion => !DesktopCapture.UsesSourceSelection && CanTakeScreenshot &&
                                                      _lastScreenshotSelection is { Region.IsEmpty: false },
                GlobalActionKind.SelectRegion => CanEditRecordingSettings && !IsSelecting && !IsConfiguring,
                GlobalActionKind.RunPreset => !IsBusy && !IsSelecting && !IsConfiguring,
                GlobalActionKind.HideMainWindow => CanHideToTray && !IsSelecting && !IsConfiguring,
                GlobalActionKind.ShowPinnedImages or GlobalActionKind.HidePinnedImages
                    or GlobalActionKind.ClosePinnedImages or GlobalActionKind.TogglePinnedImages =>
                    _pinnedImages.HasImages && !IsSelecting,
                GlobalActionKind.RestoreLastPinnedImage => _pinnedImages.CanRestoreClosed && !IsSelecting &&
                                                           !IsConfiguring,
                GlobalActionKind.PinClipboard => !IsSelecting && !IsConfiguring,
                GlobalActionKind.ToggleGlobalShortcuts => !IsConfiguring,
                GlobalActionKind.OpenScriptEditor or GlobalActionKind.OpenImageEditor
                    or GlobalActionKind.OpenVideoRecorder or
                    GlobalActionKind.OpenOutputDirectory or GlobalActionKind.ShowMainWindow =>
                    !IsSelecting && !IsConfiguring,
                _ => false,
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
                switch (action.Kind)
                {
                    case GlobalActionKind.SelectRegion:
                        await SelectAsync();
                        break;
                    case GlobalActionKind.ToggleRecording:
                    case GlobalActionKind.StartRecording:
                    case GlobalActionKind.StopRecording:
                        await ToggleRecordingAsync();
                        break;
                    case GlobalActionKind.Screenshot:
                    case GlobalActionKind.ScreenshotAndCopy:
                    case GlobalActionKind.ScreenshotAndPin:
                    case GlobalActionKind.ScreenshotAndSave:
                    case GlobalActionKind.ScreenshotQuickSave:
                    case GlobalActionKind.ScreenshotAndCopyText:
                    case GlobalActionKind.ScreenshotAndScroll:
                    case GlobalActionKind.CaptureActiveWindow:
                    case GlobalActionKind.CaptureLastRegion:
                        await ExecuteScreenshotActionAsync(action);
                        break;
                    case GlobalActionKind.RunPreset:
                        var presets = PresetCatalog.Load([action.Parameter]);
                        var preset = string.IsNullOrWhiteSpace(action.PresetName)
                            ? presets.Single()
                            : presets.Single(item => item.Name == action.PresetName);
                        ActivatePreset(preset,
                            parameters: ScriptParameters.Parse(action.Arguments.Select(pair =>
                                pair.Key + "=" + pair.Value)));
                        break;
                    case GlobalActionKind.OpenScriptEditor:
                        await OpenEditorAsync(string.IsNullOrWhiteSpace(action.Parameter) ? null : action.Parameter);
                        break;
                    case GlobalActionKind.OpenImageEditor:
                        await ShowAnimationEditorAsync(string.IsNullOrWhiteSpace(action.Parameter)
                            ? null
                            : action.Parameter);
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
                        _pinnedImages.RestoreLastClosed();
                        break;
                    case GlobalActionKind.PinClipboard:
                        await PinActionClipboardAsync();
                        break;
                    case GlobalActionKind.ClosePinnedImages:
                        _pinnedImages.CloseAll();
                        break;
                    case GlobalActionKind.TogglePinnedImages:
                        if (_pinnedImages.HasVisibleImages) _pinnedImages.HideAll();
                        else _pinnedImages.ShowAll();
                        break;
                    case GlobalActionKind.ToggleGlobalShortcuts:
                        _globalShortcutsSuspended = !_globalShortcutsSuspended;
                        RefreshActionMenus();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(action));
                }
            }
            catch (Exception error)
            {
                HostLogger.ZLogError(error,
                    $"Global action failed. Kind={action.Kind.ToString()} State={CurrentState}");
                if (!IsClosing) SetStatus(error.Message);
            }
        }

        private void RebuildActionMenus()
        {
            if (_tray?.Menu is not { } menu || _trayActionAnchor is null) return;
            foreach (var (_, item) in _actionMenus) menu.Items.Remove(item);
            _actionMenus.Clear();
            var index = 0;
            foreach (var action in Settings.GlobalActions)
            {
                if (!action.Enabled || !action.ShowInTray) continue;
                var item = new NativeMenuItem(action.Name) { Gesture = ParseTrayGesture(action.Shortcut) };
                item.Click += async (_, _) => await ExecuteActionAsync(action);
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

            _trayActionAnchor.IsVisible = _actionMenus.Count > 0;
            foreach (var (action, item) in _actionMenus)
            {
                item.Header = GlobalActions.Label(action);
                item.IsVisible = true;
                item.IsEnabled = CanExecuteAction(action);
                item.IsChecked = action.Kind == GlobalActionKind.ToggleGlobalShortcuts && _globalShortcutsSuspended;
            }
        }
    }
}
