using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class VideoRecordingWindow : Window
    {
        private static readonly int[] QualityValues = [18, 23, 28];
        private readonly Button _cancel;

        private readonly TextBlock _colorStatus = new()
            { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Error };

        private readonly CheckBox _cursor = new();
        private readonly TextBox _directory = new() { MinWidth = 0 };

        private readonly ComboBox _format = new()
            { ItemsSource = new[] { "MP4 (H.264)", "WebM (VP9)" }, SelectedIndex = 0 };

        private readonly CompactNumberInput _fps = new() { Minimum = 1, Maximum = 100, Width = 80, Height = 28 };
        private readonly ApplicationHost _host;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly CheckBox _microphone = new();
        private readonly ComboBox _microphoneDevice = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBlock _microphoneDeviceStatus = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Button _open;
        private readonly StackPanel _parameters = new() { Spacing = 10 };
        private readonly ProgressBar _progress = new() { Height = 4, IsVisible = false, IsIndeterminate = true };
        private readonly ComboBox _quality = new();
        private readonly TextBlock _region = new() { TextWrapping = TextWrapping.Wrap };
        private readonly CompactNumberInput _scale = new() { Minimum = 1, Maximum = 100, Width = 80, Height = 28 };
        private readonly Button _start;
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Button _stop;
        private readonly CheckBox _systemAudio = new();
        private readonly ComboBox _systemDevice = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBlock _systemDeviceStatus = new() { TextWrapping = TextWrapping.Wrap };
        private bool _allowClose;
        private VideoRecordingCapabilities? _capabilities;
        private bool _closed;
        private string? _microphoneDeviceError;
        private List<AudioCaptureDevice> _microphoneDevices = [];
        private Task<RecordingOutcome>? _operation;
        private bool _preparing;
        private string? _savedPath;
        private bool _selecting;
        private string? _systemDeviceError;
        private List<AudioCaptureDevice> _systemDevices = [];

        public VideoRecordingWindow(ApplicationHost host)
        {
            _host = host;
            var settings = host.Settings.VideoRecording;
            Title = UiText.Get("VideoRecorderTitle");
            Width = 650;
            Height = 540;
            MinWidth = 560;
            MinHeight = 460;
            Background = AppTheme.Workspace;
            Foreground = AppTheme.Ink;
            FontSize = 14;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _format.SelectedIndex = (int)settings.Container;
            _fps.Value = Math.Clamp(settings.Fps, 1, 100);
            _scale.Value = Math.Clamp(settings.Divisor, 1, 100);
            _quality.ItemsSource = new[]
                { UiText.Get("VideoQualityHigh"), UiText.Get("VideoQualityBalanced"), UiText.Get("VideoQualitySmall") };
            _quality.SelectedIndex = Array.IndexOf(QualityValues, settings.Quality);
            if (_quality.SelectedIndex < 0) _quality.SelectedIndex = 1;
            _directory.Text = string.IsNullOrWhiteSpace(settings.OutputDirectory)
                ? host.Settings.OutputDirectory
                : settings.OutputDirectory;
            _systemAudio.IsChecked = settings.SystemAudio && OperatingSystem.IsWindows();
            _microphone.IsChecked = settings.Microphone && OperatingSystem.IsWindows();
            _cursor.IsChecked = host.Settings.Cursor;
            _cursor.IsCheckedChanged += (_, _) => _host.SetCaptureCursor(_cursor.IsChecked == true);
            _cursor.Bind(ContentProperty, LiveText.Resource("CaptureCursor"));
            _systemAudio.Bind(ContentProperty, LiveText.Resource("VideoSystemAudio"));
            _microphone.Bind(ContentProperty, LiveText.Resource("VideoMicrophone"));
            _systemAudio.IsEnabled = _microphone.IsEnabled = OperatingSystem.IsWindows();
            _systemAudio.IsCheckedChanged += (_, _) => Refresh();
            _microphone.IsCheckedChanged += (_, _) => Refresh();
            _format.SelectionChanged += (_, _) => Refresh();
            _systemDevice.SelectionChanged += (_, _) => Refresh();
            _microphoneDevice.SelectionChanged += (_, _) => Refresh();
            var selection = new WrapPanel { Orientation = Orientation.Horizontal };
            selection.Children.Add(Button("VideoSelectScreen", () => SelectAsync(CaptureSourceKind.Screen)));
            selection.Children.Add(Button("VideoSelectWindow", () => SelectAsync(CaptureSourceKind.Window)));
            _parameters.Children.Add(Row("VideoTarget",
                new StackPanel { Spacing = 5, Children = { _region, selection } }));
            var formats = new Grid { ColumnDefinitions = new("*,Auto") };
            formats.Children.Add(_format);
            formats.Children.Add(InColumn(Button("VideoRefresh", () => PrepareAsync(host.Settings.VideoRecording)), 1));
            _parameters.Children.Add(Row("VideoFormat", new StackPanel
                { Spacing = 4, Children = { formats, _colorStatus } }));
            var timing = new WrapPanel
            {
                Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 5,
                Children = { _fps, Label("VideoScale"), _scale, _cursor },
            };
            _parameters.Children.Add(Row("VideoFps", timing));
            _parameters.Children.Add(Row("VideoQuality", _quality));
            _parameters.Children.Add(new Grid
            {
                ColumnDefinitions = new("160,*"), Children =
                {
                    _systemAudio, InColumn(new StackPanel
                        { Spacing = 3, Children = { _systemDevice, _systemDeviceStatus } }, 1),
                },
            });
            _parameters.Children.Add(new Grid
            {
                ColumnDefinitions = new("160,*"), Children =
                {
                    _microphone, InColumn(new StackPanel
                        { Spacing = 3, Children = { _microphoneDevice, _microphoneDeviceStatus } }, 1),
                },
            });
            if (!OperatingSystem.IsWindows()) _parameters.Children.Add(Label("VideoAudioUnavailable"));
            var folder = new Grid { ColumnDefinitions = new("*,Auto") };
            folder.Children.Add(_directory);
            folder.Children.Add(InColumn(Button("VideoBrowse", BrowseAsync), 1));
            _parameters.Children.Add(Row("VideoOutputDirectory", folder));
            _start = Button("VideoStart", StartAsync);
            _stop = Button("VideoStop", () => host.StopOwnedVideoAsync(_operation, false));
            _cancel = Button("VideoCancel", () => host.StopOwnedVideoAsync(_operation, true));
            _open = Button("VideoOpenOutput", OpenSavedAsync);
            var actions = new WrapPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { _open, _cancel, _stop, _start },
            };
            var root = new Grid { RowDefinitions = new("*,Auto,Auto,Auto"), Margin = new(16) };
            root.Children.Add(new ScrollViewer { Content = _parameters });
            Grid.SetRow(_progress, 1);
            root.Children.Add(_progress);
            _status.Margin = new(0, 10);
            Grid.SetRow(_status, 2);
            root.Children.Add(_status);
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);
            Content = root;
            host.Changed += Refresh;
            UiText.LanguageChanged += RefreshLanguage;
            Opened += async (_, _) => await PrepareAsync(settings);
            Closing += async (_, args) =>
            {
                if (_allowClose || _operation is not { IsCompleted: false }) return;
                args.Cancel = true;
                try
                {
                    await host.StopOwnedVideoAsync(_operation, false);
                }
                catch (Exception error)
                {
                    host.SetStatus(error.Message);
                }

                _allowClose = true;
                Close();
            };
            Closed += (_, _) =>
            {
                _closed = true;
                _lifetime.Cancel();
                _lifetime.Dispose();
                host.Changed -= Refresh;
                UiText.LanguageChanged -= RefreshLanguage;
            };
            _status.Text = UiText.Get("VideoChecking");
            RefreshLanguage();
            Refresh();
        }

        internal void CloseForShutdown()
        {
            _allowClose = true;
            Close();
        }

        private static Control InColumn(Control control, int column)
        {
            Grid.SetColumn(control, column);
            return control;
        }

        private static TextBlock Label(string key)
        {
            var label = new TextBlock
                { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            label.Bind(TextBlock.TextProperty, LiveText.Resource(key));
            return label;
        }

        private static Grid Row(string key, Control value)
        {
            return new() { ColumnDefinitions = new("160,*"), Children = { Label(key), InColumn(value, 1) } };
        }

        private Button Button(string key, Func<Task> action)
        {
            var button = new Button { Margin = new(3), Padding = new(10, 5), MinHeight = 30 };
            var label = Label(key);
            var geometry = key switch
            {
                "VideoStart" => "M5,3 L21,12 5,21 Z",
                "VideoStop" => "M4,4 L20,4 20,20 4,20 Z",
                "VideoCancel" => "M4,4 L20,20 M20,4 L4,20",
                "VideoRefresh" => "M19,3 L22,8 16,8 M22,8 A10,10 0 1 0 20,19",
                "VideoSelectScreen" => ToolbarIcon.Geometry("AnimationSelection"),
                "VideoSelectWindow" => ToolbarIcon.Geometry("AnimationRectangle"),
                _ => ToolbarIcon.Geometry("EditorOpen"),
            };
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5,
                Children = { ToolbarIcon.Create(button, geometry), label },
            };
            button.Click += async (_, _) =>
            {
                try
                {
                    await action();
                }
                catch (Exception error)
                {
                    _status.Text = ApplicationHost.VideoErrorMessage(error);
                }
            };
            return button;
        }

        private void RefreshLanguage()
        {
            Title = UiText.Get("VideoRecorderTitle");
            var selected = _quality.SelectedIndex;
            _quality.ItemsSource = new[]
                { UiText.Get("VideoQualityHigh"), UiText.Get("VideoQualityBalanced"), UiText.Get("VideoQualitySmall") };
            _quality.SelectedIndex = selected < 0 ? 1 : selected;
            Refresh();
        }

        private async Task PrepareAsync(VideoRecordingPreferences settings)
        {
            if (_preparing || _closed) return;
            _preparing = true;
            _status.Text = UiText.Get("VideoChecking");
            settings = settings with
            {
                SystemDevice = _systemDevices.ElementAtOrDefault(_systemDevice.SelectedIndex)?.Id ??
                               settings.SystemDevice,
                MicrophoneDevice = _microphoneDevices.ElementAtOrDefault(_microphoneDevice.SelectedIndex)?.Id ??
                                   settings.MicrophoneDevice,
            };
            _systemDevices = [];
            _microphoneDevices = [];
            _systemDeviceError = _microphoneDeviceError = null;
            _capabilities = null;
            _systemDevice.ItemsSource = null;
            _microphoneDevice.ItemsSource = null;
            Refresh();
            try
            {
                _capabilities = await VideoRecordingCapabilities.ProbeAsync(_lifetime.Token);
                if (_capabilities.Executable is null) _status.Text = UiText.Get("FfmpegMissing");
                else if (!_capabilities.Mp4 && !_capabilities.WebM)
                    _status.Text = UiText.Get("VideoEncoderUnavailable");
                else
                    _format.SelectedIndex = (_format.SelectedIndex, _capabilities.Mp4, _capabilities.WebM) switch
                    {
                        (0, false, _) => 1,
                        (1, _, false) => 0,
                        _ => _format.SelectedIndex,
                    };
                if (OperatingSystem.IsWindows())
                    await Task.WhenAll(RefreshAudioDevicesAsync(true, settings.SystemDevice),
                        RefreshAudioDevicesAsync(false, settings.MicrophoneDevice));
            }
            catch (OperationCanceledException) when (_closed)
            {
            }
            catch (Exception error)
            {
                if (!_closed) _status.Text = ApplicationHost.VideoErrorMessage(error);
            }
            finally
            {
                _preparing = false;
                if (!_closed) Refresh();
            }
        }

        private static List<AudioCaptureDevice> WithDefaultDevice(IReadOnlyList<AudioCaptureDevice> devices,
            string? savedId)
        {
            List<AudioCaptureDevice> choices =
                [new("", UiText.Get("VideoDefaultDevice"), devices.Any(device => device.IsDefault)), .. devices];
            if (!string.IsNullOrEmpty(savedId) && devices.All(device => device.Id != savedId))
                choices.Add(new(savedId, UiText.Get("VideoDeviceUnavailable"), false));
            return choices;
        }

        private async Task SelectAsync(CaptureSourceKind source)
        {
            if (_host.IsBusy || _selecting) return;
            _selecting = true;
            try
            {
                await CaptureWindowVisibility.HideAsync(this, _lifetime.Token);
                await _host.SelectSourceAsync(source);
            }
            finally
            {
                _selecting = false;
                if (!_closed && !_host.IsClosing) WindowActivation.Show(this);
                Refresh();
            }
        }

        private async Task BrowseAsync()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new()
                { Title = UiText.Get("ChooseOutput"), AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) _directory.Text = path;
        }

        private async Task StartAsync()
        {
            Refresh();
            if (!_start.IsEnabled || _host.Region is not { } region || _operation is { IsCompleted: false }) return;
            var capture = new RecordingOptions(region, (int)(_fps.Value ?? 30), (int)(_scale.Value ?? 1),
                _cursor.IsChecked == true);
            var options = new VideoRecordingOptions(capture)
            {
                ColorMode = _host.Settings.CaptureColorMode, ToneMap = _host.Settings.CaptureToneMap,
                Container = (VideoContainer)_format.SelectedIndex,
                Quality = QualityValues[Math.Clamp(_quality.SelectedIndex, 0, QualityValues.Length - 1)],
            };
            var directory = Path.GetFullPath(_directory.Text ?? "");
            var path = Path.Combine(directory,
                "Video-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
                (options.Container == VideoContainer.Mp4 ? ".mp4" : ".webm"));
            var systemId = _systemDevices.ElementAtOrDefault(_systemDevice.SelectedIndex)?.Id;
            var microphoneId = _microphoneDevices.ElementAtOrDefault(_microphoneDevice.SelectedIndex)?.Id;
            _host.SaveVideoPreferences(new()
            {
                Container = options.Container, Fps = capture.Fps, Divisor = capture.Divisor, Quality = options.Quality,
                SystemAudio = _systemAudio.IsChecked == true, Microphone = _microphone.IsChecked == true,
                SystemDevice = systemId, MicrophoneDevice = microphoneId, OutputDirectory = directory,
            });
            _operation = _host.StartVideoRecordingAsync(options, path, _systemAudio.IsChecked == true, systemId,
                _microphone.IsChecked == true, microphoneId, true);
            _savedPath = null;
            Refresh();
            var result = await _operation;
            if (result.Error is { } error)
            {
                _status.Text = ApplicationHost.VideoErrorMessage(error);
            }
            else if (result.Cancelled)
            {
                _status.Text = UiText.Get("Cancelled");
            }
            else
            {
                _savedPath = path;
                _status.Text = UiText.Format("VideoSaved", path);
            }

            Refresh();
        }

        private Task OpenSavedAsync()
        {
            if (_savedPath is { } path) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return Task.CompletedTask;
        }

        private void Refresh()
        {
            if (_closed) return;
            _cursor.IsChecked = _host.Settings.Cursor;
            var ownsOperation = _operation is { IsCompleted: false };
            var available = _host is { IsBusy: false, IsSelecting: false, IsConfiguring: false, IsClosing: false } &&
                            !_selecting;
            _parameters.IsEnabled = available;
            _region.Text = _host.Region is { } region
                ? $"{region.X}, {region.Y} · {region.Width} × {region.Height}"
                : UiText.Get("VideoNoRegion");
            _systemDevice.IsEnabled = _systemAudio.IsChecked == true;
            _microphoneDevice.IsEnabled = _microphone.IsChecked == true;
            ToolTip.SetTip(_systemDevice, _systemDevice.SelectedItem);
            ToolTip.SetTip(_microphoneDevice, _microphoneDevice.SelectedItem);
            var hasAudio = _systemAudio.IsChecked == true || _microphone.IsChecked == true;
            var audioSupported = !hasAudio ||
                                 (_format.SelectedIndex == 0 ? _capabilities?.Mp4Audio : _capabilities?.WebMAudio) ==
                                 true;
            var videoSupported = _host.Settings.CaptureColorMode == OutputColorMode.Hdr
                ? _format.SelectedIndex == 0 ? _capabilities?.Mp4Hdr : _capabilities?.WebMHdr
                : _format.SelectedIndex == 0
                    ? _capabilities?.Mp4
                    : _capabilities?.WebM;
            var supported = videoSupported == true &&
                            audioSupported;
            var systemReady = _systemDevices.ElementAtOrDefault(_systemDevice.SelectedIndex)?.Available == true;
            var microphoneReady =
                _microphoneDevices.ElementAtOrDefault(_microphoneDevice.SelectedIndex)?.Available == true;
            _systemDeviceStatus.IsVisible = _systemAudio.IsChecked == true && !systemReady;
            _microphoneDeviceStatus.IsVisible = _microphone.IsChecked == true && !microphoneReady;
            _systemDeviceStatus.Text = _systemDeviceError ??
                                       UiText.Get(_preparing ? "VideoChecking" : "VideoDeviceUnavailable");
            _microphoneDeviceStatus.Text = _microphoneDeviceError ??
                                           UiText.Get(_preparing ? "VideoChecking" : "VideoDeviceUnavailable");
            var audioReady = (_systemAudio.IsChecked != true || systemReady) &&
                             (_microphone.IsChecked != true || microphoneReady);
            var colorError = VideoCaptureSupport.ColorError(_host.Settings.CaptureColorMode,
                (VideoContainer)_format.SelectedIndex, _capabilities);
            _colorStatus.Text = UiText.Get(colorError ?? "ColorMode" + _host.Settings.CaptureColorMode);
            _colorStatus.IsVisible = true;
            _start.IsEnabled = available && !_preparing && audioReady && _host.Region is not null && supported &&
                               colorError is null;
            if (available && !_preparing && _capabilities?.Executable is not null)
            {
                if (!supported) _status.Text = UiText.Get("VideoEncoderUnavailable");
                else if (_status.Text == UiText.Get("VideoChecking") ||
                         _status.Text == UiText.Get("VideoEncoderUnavailable")) _status.Text = UiText.Get("Ready");
            }

            _stop.IsVisible = _cancel.IsVisible = ownsOperation;
            _stop.IsEnabled = ownsOperation && _host.IsRecording;
            _cancel.IsEnabled = ownsOperation;
            _start.IsVisible = !ownsOperation;
            _open.IsVisible = _savedPath is not null;
            _progress.IsVisible = ownsOperation;
            if (ownsOperation && _host.LatestProgress is { } progress)
                _status.Text = progress.Stage is RecordingStage.Recording or RecordingStage.Live
                    ? UiText.Format("VideoProgress", progress.ElapsedSeconds, progress.Captured, progress.CapturedFps)
                    : UiText.Get("Stage" + progress.Stage);
        }
    }
}
