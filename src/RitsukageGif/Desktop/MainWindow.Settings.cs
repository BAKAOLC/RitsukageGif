using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class MainWindow
    {
        public void SaveCommon()
        {
            if (!_host.CanEditRecordingSettings) return;
            _host.SetCommon((int)(_fps.Value ?? 20), (int)(_scale.Value ?? 2),
                (OutputFormat)Math.Max(0, _format.SelectedIndex), _cursor.IsChecked == true);
        }

        private void RefreshPresets()
        {
            var menu = new ContextMenu();
            var screen = new MenuItem { Name = "SelectScreenRegion", Header = UiText.Get("SelectScreenRegion") };
            screen.Click += async (_, _) => await _host.SelectSourceAsync(CaptureSourceKind.Screen);
            var window = new MenuItem { Name = "SelectWindowContent", Header = UiText.Get("SelectWindowContent") };
            window.Click += async (_, _) => await _host.SelectSourceAsync(CaptureSourceKind.Window);
            menu.Items.Add(screen);
            menu.Items.Add(window);
            menu.Items.Add(new Separator());
            try
            {
                foreach (var preset in PresetCatalog.Load(Settings.PresetFiles))
                {
                    var item = new MenuItem { Header = preset.Name };
                    item.Click += async (_, _) =>
                    {
                        var reply = await _host.ExecuteCommandAsync(["preset", "activate", preset.Name]);
                        if (!reply.Success) _host.SetStatus(reply.Message);
                    };
                    menu.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                _host.SetStatus(ex.Message);
            }

            var editor = new MenuItem { Header = UiText.Get("EditorTitle") };
            editor.Click += (_, _) => _host.OpenEditor(null);
            menu.Items.Add(editor);
            var stop = new MenuItem { Header = UiText.Get("StopPreset") };
            stop.Click += async (_, _) => await _host.ExecuteCommandAsync(["preset", "stop"]);
            menu.Items.Add(stop);
            _select.ContextMenu = menu;
        }

        public async Task ConfigureAsync()
        {
            if (!_host.BeginConfigure()) return;
            try
            {
                SaveCommon();
                var result = await new SettingsWindow(Settings, _host.OpenSettingsEditorAsync)
                    .ShowDialog<AppSettings?>(this);
                if (result != null)
                {
                    _host.UpdateSettings(result);
                    RefreshPresets();
                    ApplyBackground();
                }
            }
            catch (Exception ex)
            {
                _host.SetStatus(ex.Message);
            }
            finally
            {
                _host.EndConfigure();
            }
        }
    }
}
