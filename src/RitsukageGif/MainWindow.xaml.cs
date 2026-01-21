using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using NHotkey;
using NHotkey.Wpf;
using RitsukageGif.CaptureProvider.RecordFrame;
using RitsukageGif.Class;
using RitsukageGif.Enums;
using RitsukageGif.Structs;
using RitsukageGif.Windows;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;

namespace RitsukageGif
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public static readonly string TempPath = Path.Combine(Path.GetTempPath(), "RitsukageGif");

        private static MainWindow? _instance;
        private readonly RecordFrameProvider _recordFrameProvider = new();

        private AudioPlayer? _audioPlayer;

        private bool _canBeginRecord;
        private bool _canChangeRegion;
        private string? _currentGifPath;

        private int _delay;

        private DataObject? _lastRecordGifClipboardData;
        private OutputFormat _outputFormat = OutputFormat.Gif;
        private CancellationTokenSource? _processingCancellationTokenSource;
        private CancellationTokenSource? _recordingCancellationTokenSource;

        public MainWindow()
        {
            _instance = this;
            InitializeComponent();
            VersionLabel.Content = $"ver {Assembly.GetExecutingAssembly().GetName().Version}";
        }

        public HotKey HotKeyPushRecordGif { get; private set; }
        public HotKey HotKeySelectRegion { get; private set; }

        public SelectedRegionResult? Region { get; private set; }

        public int Fps
        {
            get;
            private set
            {
                field = value;
                _delay = 1000 / field;
            }
        }

        public int Scale { get; private set; }

        public bool RecordCursor { get; private set; }

        public bool Recording { get; private set; }

        private Control[] RecordingDisabledControls =>
        [
            RegionSelectButton,
            GifScaleInteger,
            GifFrameInteger,
            RecordCursorCheckBox,
            OutputFormatComboBox,
        ];

        public static void ShutdownAllTasks()
        {
            if (_instance == null) return;
            AboutWindow.CloseInstance();
            RemoveHotKeys();
            _instance._recordingCancellationTokenSource?.Cancel();
            _instance._processingCancellationTokenSource?.Cancel();
            _instance._canBeginRecord = false;
            _instance._canChangeRegion = false;
            _instance.IsEnabled = false;
        }

        public static MainWindow? GetInstance()
        {
            return _instance;
        }

        public static void CloseInstance()
        {
            _instance?.Close();
            _instance = null;
        }

        private static bool CheckOsVersion()
        {
            //最低支持Windows 10
            var osVersion = Environment.OSVersion;
            switch (osVersion.Platform)
            {
                case PlatformID.Win32NT:
                    if (osVersion.Version.Major >= 10) return true;

                    break;
                case PlatformID.Win32S:
                case PlatformID.Win32Windows:
                case PlatformID.WinCE:
                case PlatformID.Unix:
                case PlatformID.Xbox:
                case PlatformID.MacOSX:
                case PlatformID.Other:
                default:
                    break;
            }

            return false;
        }

        private void SetDefaultConfig()
        {
            if (!string.IsNullOrEmpty(Settings.Default.HotKeyRecordGif))
                if (HotKey.TryParse(Settings.Default.HotKeyRecordGif, out var hotKey))
                    HotKeyPushRecordGif = hotKey;

            if (!string.IsNullOrEmpty(Settings.Default.HotKeySelectRegion))
                if (HotKey.TryParse(Settings.Default.HotKeySelectRegion, out var hotKey))
                    HotKeySelectRegion = hotKey;

            GifFrameInteger.Value = Fps = Math.Max(Math.Min(Settings.Default.RecordFrameFps, 30), 1);
            GifScaleInteger.Value = Scale = Math.Max(Math.Min(Settings.Default.RecordFrameScale, 100), 1);
            RecordCursorCheckBox.IsChecked = RecordCursor = Settings.Default.RecordCursor;
            UseFfmpegEncodeCheckBox.IsChecked = Settings.Default.UseFfmpegEncoder;

            var formatIndex = Math.Max(0, Math.Min(2, Settings.Default.OutputFormat));
            OutputFormatComboBox.SelectedIndex = formatIndex;
            _outputFormat = (OutputFormat)formatIndex;
        }

        private void SaveConfig()
        {
            Settings.Default.RecordCursor = RecordCursor;
            Settings.Default.OutputFormat = (int)_outputFormat;
            Settings.Default.Save();
        }

        private void ApplyBackground()
        {
            var image = Settings.Default.BackgroundImage;
            BackgroundImage.Source = string.IsNullOrWhiteSpace(image) ? null : new BitmapImage(new(image));
        }

        private void StartRecording()
        {
            if (Region == null) return;
            _canChangeRegion = false;
            Recording = true;
            RecordButton.Content = "停止录制";
            RecordButton.Background = Brushes.Red;
            foreach (var control in RecordingDisabledControls)
                control.IsEnabled = false;
            _recordingCancellationTokenSource?.Cancel();
            _processingCancellationTokenSource?.Cancel();
            SaveConfig();
            StartRecordingTaskAsync();
            if (string.IsNullOrWhiteSpace(Settings.Default.StartRecordingSoundFile)) return;
            _audioPlayer?.StopAudio();
            _audioPlayer?.PlayAudio(Settings.Default.StartRecordingSoundFile);
        }

        private Task StartRecordingTaskAsync()
        {
            if (Region == null) return Task.CompletedTask;

            var tokenRecording = new CancellationTokenSource();
            var tokenProcessing = new CancellationTokenSource();
            _recordingCancellationTokenSource = tokenRecording;
            _processingCancellationTokenSource = tokenProcessing;
            _recordFrameProvider.SetOutputFormat(_outputFormat);
            var path = GenerateTempFileName(_recordFrameProvider.GetFileExtension());
            var info = _recordFrameProvider.BeginRecord(path, Region.Converted, _delay, (double)1 / Scale, RecordCursor,
                tokenRecording.Token, tokenProcessing.Token);
            return Task.Run(async () =>
            {
                Dispatcher.Invoke(() => { GifEncodingLabelGrid.Visibility = Visibility.Visible; });
                do
                {
                    await Task.Delay(30, tokenProcessing.Token).ConfigureAwait(false);
                    Dispatcher.Invoke(() =>
                    {
                        if (tokenProcessing.IsCancellationRequested)
                            GifEncodingLabelGrid.Visibility = Visibility.Hidden;
                        else
                            GifFramesLabel.Content = $"{info.ProcessedFrames} / {info.Frames}";
                    });
                } while (!(info.Completed || tokenProcessing.IsCancellationRequested));

                Dispatcher.Invoke(() => { GifEncodingLabelGrid.Visibility = Visibility.Hidden; });
                if (tokenProcessing.IsCancellationRequested) return;
                SetShowInfo(path);
            }, tokenProcessing.Token);
        }

        private void SetShowInfo(string path)
        {
            var file = new FileInfo(path);
            var dataObject = new DataObject();
            var byteDataQ =
                Encoding.UTF8.GetBytes(
                    $"<QQRichEditFormat><Info version=\"1001\"></Info><EditElement type=\"1\" filepath=\"{path}\" shortcut=\"\"></EditElement><EditElement type=\"0\"><![CDATA[]]></EditElement></QQRichEditFormat>");
            var byteDataH =
                Encoding.UTF8.GetBytes(
                    $"<!DOCTYPE HTML PUBLIC \\\"-//W3C//DTD HTML 4.0 Transitional//EN\\\"><html><body><!--StartFragment--><p><img src=\\\"file:///{path}\\\"></p><!--EndFragment--></body></html>");
            var dataMemoryStreamQ = new MemoryStream(byteDataQ);
            var dataMemoryStreamH = new MemoryStream(byteDataH);
            dataObject.SetData("QQ_Unicode_RichEdit_Format", dataMemoryStreamQ);
            dataObject.SetData("QQ_RichEdit_Format", dataMemoryStreamQ);
            dataObject.SetData("HTML Format", dataMemoryStreamH);
            dataObject.SetFileDropList([path]);
            _lastRecordGifClipboardData = dataObject;
            Dispatcher.Invoke(() =>
            {
                Clipboard.SetDataObject(dataObject, true);
                switch (file.Length)
                {
                    case > 6 * 1024 * 1024:
                        GifSizeLabel.Foreground = Brushes.DarkRed;
                        GifSizeLabel.FontWeight = FontWeights.Bold;
                        break;
                    case > 3 * 1024 * 1024:
                        GifSizeLabel.Foreground = Brushes.Red;
                        GifSizeLabel.FontWeight = FontWeights.Normal;
                        break;
                    default:
                        GifSizeLabel.Foreground = Brushes.Black;
                        GifSizeLabel.FontWeight = FontWeights.Normal;
                        break;
                }

                GifSizeLabel.Content = file.Length > 1024 * 1024
                    ? $"{(double)file.Length / 1024 / 1024:F2}MB"
                    : file.Length > 1024
                        ? $"{(double)file.Length / 1024:F2}KB"
                        : (object)$"{(double)file.Length:F2}B";

                AnimatedView.Visibility = Visibility.Visible;
                _currentGifPath = path;
                AnimatedView.SourcePath = path;
            });
        }

        private void StopRecording()
        {
            _canChangeRegion = true;
            Recording = false;
            RecordButton.Content = "开始录制";
            RecordButton.Background = Brushes.White;
            foreach (var control in RecordingDisabledControls)
                control.IsEnabled = true;
            _recordingCancellationTokenSource?.Cancel();
            if (string.IsNullOrWhiteSpace(Settings.Default.StopRecordingSoundFile)) return;
            _audioPlayer?.StopAudio();
            _audioPlayer?.PlayAudio(Settings.Default.StopRecordingSoundFile);
        }

        private async Task OpenRegionSelectWindowAsync()
        {
            var regionSelect = RegionSelect.Begin();
            var (confirm, region) = await regionSelect.WaitForResultAsync().ConfigureAwait(false);
            _canChangeRegion = true;
            if (confirm) Region = region;

            Dispatcher.Invoke(() =>
            {
                if (Region != null)
                {
                    _canBeginRecord = true;
                    RecordButton.IsEnabled = true;
                }
                else
                {
                    RecordButton.IsEnabled = false;
                }
            });
        }

        private void RegisterHotKeys()
        {
            bool success1 = true, success2 = true;
            try
            {
                if (HotKeyPushRecordGif != default)
                    HotkeyManager.Current.AddOrReplace("PushRecordGif",
                        HotKeyPushRecordGif.Key,
                        HotKeyPushRecordGif.ModifierKeys,
                        OnHotKey_PushRecordGif);
            }
            catch
            {
                success1 = false;
            }

            try
            {
                if (HotKeySelectRegion != default)
                    HotkeyManager.Current.AddOrReplace("SelectRegion",
                        HotKeySelectRegion.Key,
                        HotKeySelectRegion.ModifierKeys,
                        OnHotKey_SelectRegion);
            }
            catch
            {
                success2 = false;
            }

            if (success1 && success2) return;
            var sb = new StringBuilder();
            sb.AppendLine("以下快捷键注册失败，请检查是否有其他程序占用了快捷键。");
            if (!success1)
                sb.AppendLine($"{HotKeyPushRecordGif}：开始/停止录制");
            if (!success2)
                sb.AppendLine($"{HotKeySelectRegion}：选择录制区域");
            Task.Run(() =>
            {
                MessageBox.Show(sb.ToString(),
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }).ConfigureAwait(false);
        }

        private static void RemoveHotKeys()
        {
            HotkeyManager.Current.Remove("PushRecordGif");
            HotkeyManager.Current.Remove("SelectRegion");
        }

        private void OnHotKey_PushRecordGif(object? sender, HotkeyEventArgs e)
        {
            if (!_canBeginRecord) return;
            RecordButton_Click(null, null!);
        }

        private void OnHotKey_SelectRegion(object? sender, HotkeyEventArgs e)
        {
            if (!_canChangeRegion) return;
            RegionSelectButton_Click(null, null!);
        }

        private void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            _ = new Mutex(true, "RitsukageGif_SingleInstance", out var createdNew);
            if (!createdNew)
            {
                Hide();
                MessageBox.Show("程序已经在运行中", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            if (CheckOsVersion())
            {
                _audioPlayer = new();
                CleanUpTempPath();
                SetDefaultConfig();
                RegisterHotKeys();
                _canBeginRecord = false;
                _canChangeRegion = true;
                ApplyBackground();
#if !DEBUG
                _ = Task.Run(Updater.CheckUpdateAsync);
#endif
            }
            else
            {
                var result = MessageBox.Show("本程序目前必须在 Windows 10 及以上版本才能运行", "错误", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                if (result == MessageBoxResult.OK) Close();
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            SaveConfig();
            AboutWindow.CloseInstance();
            RemoveHotKeys();
            _recordingCancellationTokenSource?.Cancel();
            _processingCancellationTokenSource?.Cancel();
            _canBeginRecord = false;
            _canChangeRegion = false;
        }

        private void RecordButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!_canBeginRecord) return;
            if (Recording)
                StopRecording();
            else
                StartRecording();
        }

        private void RegionSelectButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!_canChangeRegion) return;
            _canBeginRecord = false;
            _canChangeRegion = false;
            OpenRegionSelectWindowAsync().ConfigureAwait(false);
        }

        private void AboutButton_Click(object? sender, RoutedEventArgs e)
        {
            AboutWindow.Begin();
        }

        private void GifScaleInteger_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue == null) return;
            var value = (int)e.NewValue;
            value = Math.Max(1, Math.Min(100, value));
            Scale = value;
        }

        private void GifFrameInteger_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue == null) return;
            var value = (int)e.NewValue;
            value = Math.Max(1, Math.Min(30, value));
            Fps = value;
        }

        private void RecordCursorCheckBox_Checked(object? sender, RoutedEventArgs e)
        {
            RecordCursor = true;
        }

        private void RecordCursorCheckBox_Unchecked(object? sender, RoutedEventArgs e)
        {
            RecordCursor = false;
        }

        private void UseFfmpegCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Settings.Default.UseFfmpegEncoder = true;
        }

        private void UseFfmpegCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Settings.Default.UseFfmpegEncoder = false;
        }

        private void OutputFormatComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (OutputFormatComboBox.SelectedIndex < 0) return;
            _outputFormat = OutputFormatComboBox.SelectedIndex switch
            {
                0 => OutputFormat.Gif,
                1 => OutputFormat.WebP,
                2 => OutputFormat.APng,
                _ => OutputFormat.Gif,
            };
        }

        private void AnimatedView_OnMouseRightButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (_lastRecordGifClipboardData == null) return;
            Clipboard.SetDataObject(_lastRecordGifClipboardData, true);
        }

        private void AnimatedView_OnPreviewMouseLeftButtonDown(object? sender,
            MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentGifPath)) return;
            if (sender is not Image image) return;
            var dataObject = new DataObject(DataFormats.FileDrop, new[] { _currentGifPath });
            DragDrop.DoDragDrop(image, dataObject, DragDropEffects.Copy);
        }

        private void Window_DpiChanged(object? sender, DpiChangedEventArgs e)
        {
            ScreenInfo.ClearCache();
        }

        private static void CleanUpTempPath()
        {
            if (!Directory.Exists(TempPath)) return;
            try
            {
                var dirInfo = new DirectoryInfo(TempPath);
                foreach (var file in dirInfo.GetFiles())
                    file.Delete();
                foreach (var dir in dirInfo.GetDirectories())
                    dir.Delete(true);
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }

        private static string GenerateTempFileName(string ext)
        {
            if (!Directory.Exists(TempPath))
                Directory.CreateDirectory(TempPath);
            return Path.Combine(TempPath,
                    string.Join(string.Empty, Guid.NewGuid().ToByteArray().Select(x => x.ToString("X2"))) + ext)
                .Replace('\\', '/');
        }
    }
}