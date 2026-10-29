using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    public sealed partial class MainWindow
    {
        private MenuItem? _managePresetsMenu;
        private ContextMenu? _presetMenu;

        private MenuItem? _sourceScreenMenu;
        private MenuItem? _sourceWindowMenu;
        private MenuItem? _stopPresetMenu;

        public void SaveCommon()
        {
            if (!_host.CanEditRecordingSettings) return;
            _host.SetCommon((int)(_fps.Value ?? 20), (int)(_scale.Value ?? 2),
                (OutputFormat)Math.Max(0, _format.SelectedIndex), _cursor.IsChecked == true);
        }

        private void RefreshPresets()
        {
            _select.ContextMenu?.Close();
            var menu = new ContextMenu();
            _sourceScreenMenu = new()
            {
                Name = "SelectScreenRegion", ToggleType = MenuItemToggleType.Radio, GroupName = "capture-source",
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("SelectScreenRegion"),
            };
            _sourceScreenMenu.Click += async (_, _) => await _host.SelectSourceAsync(CaptureSourceKind.Screen);
            _sourceWindowMenu = new()
            {
                Name = "SelectWindowContent", ToggleType = MenuItemToggleType.Radio, GroupName = "capture-source",
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("SelectWindowContent"),
            };
            _sourceWindowMenu.Click += async (_, _) => await _host.SelectSourceAsync(CaptureSourceKind.Window);
            menu.Items.Add(_sourceScreenMenu);
            menu.Items.Add(_sourceWindowMenu);
            _select.ContextMenu = menu;
            menu.Opened += (_, _) => RefreshSourceMenuState();
            _presetsButton.ContextMenu?.Close();
            _presetMenu = new() { Name = "RecordingPresets" };
            try
            {
                foreach (var preset in PresetCatalog.Load(Settings.PresetFiles))
                {
                    var item = new MenuItem
                    {
                        Header = preset.Name, Tag = preset.Name, ToggleType = MenuItemToggleType.Radio,
                        GroupName = "capture-preset",
                    };
                    item.Click += async (_, _) => await RunMainActionAsync(async () =>
                    {
                        var reply = await _host.ExecuteCommandAsync(["preset", "activate", preset.Name]);
                        if (!reply.Success) _host.SetStatus(reply.Message);
                        RefreshSourceMenuState();
                    });
                    _presetMenu.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                _host.SetStatus(ex.Message);
            }

            if (_presetMenu.Items.Count == 0)
                _presetMenu.Items.Add(new MenuItem
                {
                    IsEnabled = false,
                    [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("MainNoPresets"),
                });
            _stopPresetMenu = new()
            {
                Name = "StopRecordingPreset",
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("StopPreset"),
            };
            _stopPresetMenu.Click += async (_, _) => await RunMainActionAsync(async () =>
            {
                var reply = await _host.ExecuteCommandAsync(["preset", "stop"]);
                if (!reply.Success) _host.SetStatus(reply.Message);
            });
            _presetMenu.Items.Add(new Separator());
            _presetMenu.Items.Add(_stopPresetMenu);
            _presetMenu.Items.Add(new Separator());
            _managePresetsMenu = new()
            {
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PresetsTitle"),
            };
            _managePresetsMenu.Click += async (_, _) => await ManagePresetsAsync();
            _presetMenu.Items.Add(_managePresetsMenu);
            var editor = new MenuItem
            {
                [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("EditorTitle"),
            };
            editor.Click += (_, _) => _host.OpenEditor(null);
            _presetMenu.Items.Add(editor);
            _presetMenu.Opened += (_, _) => RefreshSourceMenuState();
            _presetsButton.ContextMenu = _presetMenu;
            RefreshSourceMenuState();
        }

        private void RefreshSourceMenuState()
        {
            if (_sourceScreenMenu is null || _sourceWindowMenu is null || _presetMenu is null ||
                _stopPresetMenu is null) return;
            _selectMenu.IsEnabled = _host is { IsClosing: false, IsSelecting: false, IsConfiguring: false };
            var canSelect = _host is { CanEditRecordingSettings: true, IsSelecting: false, IsConfiguring: false };
            _sourceScreenMenu.IsEnabled = _sourceWindowMenu.IsEnabled = canSelect;
            _sourceScreenMenu.IsChecked =
                _host.Region is not null && _host.SelectedCaptureSourceKey == "MainSourceScreen";
            _sourceWindowMenu.IsChecked =
                _host.Region is not null && _host.SelectedCaptureSourceKey == "MainSourceWindow";
            _presetsButton.IsEnabled = _host is { IsClosing: false, IsSelecting: false, IsConfiguring: false };
            _managePresetsMenu?.IsEnabled = canSelect && !_host.IsBusy;
            _stopPresetMenu.IsEnabled = _host is
                { HasActivePreset: true, IsClosing: false, IsSelecting: false, IsConfiguring: false };
            foreach (var item in _presetMenu.Items.OfType<MenuItem>())
            {
                if (item.Tag is not string name) continue;
                item.IsChecked = name == _host.ActivePresetName;
                item.IsEnabled = canSelect && !_host.IsBusy;
            }
        }

        private async Task ManagePresetsAsync()
        {
            if (!_host.BeginConfigure()) return;
            try
            {
                var value = await new PresetsWindow(Settings.PresetFiles, Settings.PresetHotkeys,
                        _host.OpenSettingsEditorAsync,
                        () =>
                        [
                            .. Settings.GlobalActions.Where(action => action.Enabled).Select(action => action.Shortcut),
                        ])
                    .ShowDialog<PresetConfiguration?>(this);
                if (value is not null)
                {
                    var previous = Settings;
                    var updated = Settings with { PresetFiles = value.Files, PresetHotkeys = value.Hotkeys };
                    _host.UpdateSettings(updated);
                    if (previous.PresetFiles.SequenceEqual(value.Files, StringComparer.Ordinal)) RefreshPresets();
                }
            }
            catch (Exception error)
            {
                _host.SetStatus(error.Message);
            }
            finally
            {
                _host.EndConfigure();
            }
        }

        private async Task ConfigureAsync()
        {
            await _host.OpenSettingsAsync();
        }

        internal void RefreshSettingsPresentation(AppSettings previous, AppSettings settings)
        {
            if (!previous.PresetFiles.SequenceEqual(settings.PresetFiles, StringComparer.Ordinal)) RefreshPresets();
            if (previous.ShowBackground != settings.ShowBackground ||
                (settings.ShowBackground && previous.BackgroundImage != settings.BackgroundImage)) ApplyBackground();
            RefreshFromHost();
        }
    }
}
