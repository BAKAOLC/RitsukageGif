using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    public sealed partial class MainWindow : Window
    {
        private readonly Button _cancel = new() { Content = UiText.Get("CancelProcessing"), IsVisible = false };
        private readonly Control[] _commonControls;
        private readonly Button _configure = new() { Content = UiText.Get("Settings") };

        private readonly CheckBox _cursor = new()
        {
            Name = "CaptureCursor", MinHeight = 0, Padding = new(0),
            Content = new TextBlock
            {
                Text = UiText.Get("CaptureCursor"), TextWrapping = TextWrapping.Wrap,
                MaxWidth = 76, FontSize = 11,
            },
        };

        private readonly ComboBox _format = new()
        {
            ItemsSource = new[] { "GIF", "WebP", "APNG" }, Width = 80, MinHeight = 0, Height = 24,
            Padding = new(4, 0), FontSize = 12,
        };

        private readonly CompactNumberInput _fps = new();
        private readonly ApplicationHost _host;
        private readonly Image _image = new() { Stretch = Stretch.Uniform, MaxHeight = 130 };
        private readonly AnimationPreview _preview;
        private readonly Button _record = new() { Content = UiText.Get("StartRecording"), IsEnabled = false };

        private readonly TextBlock _regionText = new()
            { Text = UiText.Get("SelectAreaHint"), TextWrapping = TextWrapping.Wrap };

        private readonly CompactNumberInput _scale = new();
        private readonly Button _select = new() { Content = UiText.Get("SelectArea") };
        private readonly TextBlock _size = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _status = new() { Text = UiText.Get("Ready"), TextWrapping = TextWrapping.Wrap };
        private bool _closing, _allowClose;
        private bool? _displayCursor;
        private OutputFormat? _displayFormat;
        private int _displayFps, _displayScale;
        private string? _displayPath;

        internal MainWindow(ApplicationHost host)
        {
            _host = host;
            _commonControls = [_select, _fps, _scale, _format, _cursor, _configure];
            Title = "RitsukageGif";
            Width = 287;
            Height = 420;
            MinWidth = 271;
            MinHeight = 390;
            FontSize = 12;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _preview = new(_image, _host.SetStatus);
            BuildOriginalLayout();
            RefreshPresets();
            RefreshFromHost();
            _host.Changed += RefreshFromHost;
            _select.Click += async (_, _) => await _host.SelectAsync();
            _record.Click += async (_, _) => await _host.ToggleRecordingAsync();
            _cancel.Click += (_, _) => _host.CancelRecording();
            _configure.Click += async (_, _) => await ConfigureAsync();
            _updateAvailable.Click += (_, _) => OpenAvailableUpdate();
            _cursor.Click += (_, _) =>
            {
                try
                {
                    SaveCommon();
                }
                catch (Exception exception)
                {
                    _host.SetStatus(exception.Message);
                }
            };
            _image.PointerPressed += async (_, e) =>
            {
                if (LastPath is not { } path) return;
                try
                {
                    if (e.GetCurrentPoint(_image).Properties.IsRightButtonPressed)
                    {
                        await _host.CopyFilesAsync([path]);
                        return;
                    }

                    var file = await StorageProvider.TryGetFileFromPathAsync(path);
                    if (file is null) return;
                    var data = new DataTransfer();
                    data.Add(DataTransferItem.CreateFile(file));
                    await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
                }
                catch (Exception ex)
                {
                    _host.SetStatus(ex.Message);
                }
            };
            Closing += async (_, e) =>
            {
                if (_allowClose) return;
                e.Cancel = true;
                if (_closing) return;
                _closing = true;
                IsEnabled = false;
                try
                {
                    SaveCommon();
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Saving settings during shutdown failed: {0}", ex);
                }

                try
                {
                    await _host.ShutdownAsync();
                }
                finally
                {
                    if (!_host.IsClosing)
                    {
                        _closing = false;
                        IsEnabled = true;
                    }
                }
            };
            Closed += (_, _) =>
            {
                _host.Changed -= RefreshFromHost;
                _preview.Dispose();
            };
        }

        private AppSettings Settings => _host.Settings;
        private string? LastPath => _selectedOutputPath ?? _host.LastPath;

        internal void CloseForShutdown()
        {
            _allowClose = true;
            Close();
        }

        public void RefreshFromHost()
        {
            _updateAvailable.IsVisible = _host.AvailableUpdate is not null;
            ToolTip.SetTip(_updateAvailable, _host.AvailableUpdate is { } release
                ? UiText.Format("UpdateAvailable", release.Version)
                : null);
            var settings = _host.EffectiveSettings;
            if (_displayFps != settings.Fps) _fps.Value = _displayFps = settings.Fps;
            if (_displayScale != settings.Divisor) _scale.Value = _displayScale = settings.Divisor;
            if (_displayFormat != settings.Format)
            {
                _displayFormat = settings.Format;
                _format.SelectedIndex = (int)settings.Format;
            }

            if (_displayCursor != settings.Cursor) _cursor.IsChecked = _displayCursor = settings.Cursor;
            _status.Text = _host.Status;
            RefreshOutputs();
            RefreshStreamStatus();
            _record.Content =
                UiText.Get(_host.IsRecording ? "StopRecording" : _host.IsBusy ? "Processing" : "StartRecording");
            _record.Background = _host.IsRecording ? Brushes.Red : Brushes.White;
            _record.IsEnabled = !_host.IsClosing && !_host.IsSelecting && !_host.IsConfiguring &&
                                (_host.IsRecording || (!_host.IsBusy && _host.Region is not null));
            _cancel.IsVisible = _host.IsBusy && !_host.IsRecording;
            foreach (var control in _commonControls)
                control.IsEnabled = !_host.IsBusy && !_host.IsSelecting && !_host.IsConfiguring && !_host.IsClosing;
            _regionText.Text = _host.Region is { } region
                ? UiText.Format("RegionInfo", region.X, region.Y, region.Width, region.Height)
                : UiText.Get("SelectAreaHint");
            ToolTip.SetTip(_select, _regionText.Text);
            _frames.Text = _host.LatestProgress is { } progress ? $"{progress.Encoded} / {progress.Captured}" : "N/A";
            _captureRate.Text = _host.LatestProgress is { } captureProgress
                ? UiText.Format("CaptureRateValue", captureProgress.CapturedFps)
                : "—";
            ToolTip.SetTip(_frames, _host.LatestProgress is { } details ? RecordingProgressText.Format(details) : null);
            if (_displayPath == LastPath) return;
            _displayPath = LastPath;
            if (LastPath is null) return;
            try
            {
                var bytes = new FileInfo(LastPath).Length;
                _size.Text = bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F2} MB" : $"{bytes / 1024.0:F1} KB";
                _size.Foreground = bytes > 6 * 1024 * 1024 ? Brushes.DarkRed :
                    bytes > 3 * 1024 * 1024 ? Brushes.Red : Brushes.Black;
                _size.FontWeight = bytes > 6 * 1024 * 1024 ? FontWeight.Bold : FontWeight.Normal;
            }
            catch (IOException)
            {
                _size.Text = "N/A";
            }
            catch (UnauthorizedAccessException)
            {
                _size.Text = "N/A";
            }
        }

        public async Task LoadPreviewAsync(string path)
        {
            SelectOutput(path);
            try
            {
                await _preview.LoadAsync(path);
                if (LastPath == path && _image.Source is { } image)
                    ToolTip.SetTip(_image,
                        UiText.Format("PreviewHint", (int)image.Size.Width, (int)image.Size.Height, path));
            }
            catch (Exception ex)
            {
                _host.SetStatus(UiText.Get("PreviewFailed") + ex.Message);
            }
        }

        public void PausePreview()
        {
            _preview.Pause();
        }
    }
}
