using System;
using System.Collections.Generic;
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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NHotkey;
using NHotkey.Wpf;
using RitsukageGif.CaptureProvider.RecordFrame;
using RitsukageGif.Class;
using RitsukageGif.Windows;
using WpfAnimatedGif;

namespace RitsukageGif
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IRecordFrameProvider _recordFrameProvider = new GifRecordFrameProvider();

        private readonly List<string> _tempFileList = new List<string>();

        private bool _canBeginRecord;
        private bool _canChangeRegion;
        private string _currentGifPath;

        private int _delay;

        private int _fps;
        private byte[] _lastRecordGifClipboardBytes;
        private CancellationTokenSource _processingCancellationTokenSource;
        private CancellationTokenSource _recordingCancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            VersionLabel.Content = $"ver {Assembly.GetExecutingAssembly().GetName().Version}";
        }

        public SelectedRegionResult Region { get; private set; }

        public int Fps
        {
            get => _fps;
            private set
            {
                _fps = value;
                _delay = 1000 / _fps;
            }
        }

        public int Scale { get; private set; }

        public bool RecordCursor { get; private set; }

        public bool RecordInMemory { get; private set; }

        public bool DXGIRecord { get; private set; }

        public bool Recording { get; private set; }

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
                default:
                    break;
            }

            return false;
        }

        private void SetDefaultConfig()
        {
            GifFrameInteger.Value = Fps = 20;
            GifScaleInteger.Value = Scale = 2;
            RecordCursorCheckBox.IsChecked = RecordCursor = Settings.Default.RecordCursor;
            MemoryRecordCheckBox.IsChecked = RecordInMemory = Settings.Default.MemoryRecord;
            DXGIRecordCheckBox.IsChecked = DXGIRecord = Settings.Default.ScreenFrameProvider == 1;
        }

        private void SaveConfig()
        {
            Settings.Default.RecordCursor = RecordCursor;
            Settings.Default.MemoryRecord = RecordInMemory;
            Settings.Default.ScreenFrameProvider = DXGIRecord ? 1 : 0;
            Settings.Default.Save();
        }

        private void StartRecording()
        {
            _canChangeRegion = false;
            Recording = true;
            RecordButton.Content = "停止录制";
            RecordButton.Background = Brushes.Red;
            RegionSelectButton.IsEnabled = false;
            GifScaleInteger.IsEnabled = false;
            GifFrameInteger.IsEnabled = false;
            RecordCursorCheckBox.IsEnabled = false;
            MemoryRecordCheckBox.IsEnabled = false;
            DXGIRecordCheckBox.IsEnabled = false;
            _recordingCancellationTokenSource?.Cancel();
            _processingCancellationTokenSource?.Cancel();
            StartRecordingTaskAsync();
        }

        private Task StartRecordingTaskAsync()
        {
            var tokenRecording = new CancellationTokenSource();
            var tokenProcessing = new CancellationTokenSource();
            _recordingCancellationTokenSource = tokenRecording;
            _processingCancellationTokenSource = tokenProcessing;
            var path = GenerateTempFileName(_recordFrameProvider.GetFileExtension());
            _tempFileList.Add(path);
            var info = RecordInMemory
                ? _recordFrameProvider.BeginWithMemory(path, Region.Converted, _delay, (double)1 / Scale, RecordCursor,
                    tokenRecording.Token, tokenProcessing.Token)
                : _recordFrameProvider.BeginWithoutMemory(path, Region.Converted, _delay, (double)1 / Scale,
                    RecordCursor,
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
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">");
            sb.AppendFormat(
                "<html><body><!--StartFragment--><p><img src=\"file:///{0}\"></p><!--EndFragment--></body></html>",
                path);
            _lastRecordGifClipboardBytes = Encoding.Default.GetBytes(sb.ToString());
            Dispatcher.Invoke(() =>
            {
                using (var data = new MemoryStream(_lastRecordGifClipboardBytes))
                {
                    Clipboard.SetData("Html Format", data);
                }

                if (file.Length > 6 * 1024 * 1024)
                {
                    GifSizeLabel.Foreground = Brushes.DarkRed;
                    GifSizeLabel.FontWeight = FontWeights.Bold;
                }
                else if (file.Length > 3 * 1024 * 1024)
                {
                    GifSizeLabel.Foreground = Brushes.Red;
                    GifSizeLabel.FontWeight = FontWeights.Normal;
                }
                else
                {
                    GifSizeLabel.Foreground = Brushes.Black;
                    GifSizeLabel.FontWeight = FontWeights.Normal;
                }

                GifSizeLabel.Content = file.Length > 1024 * 1024
                    ? $"{(double)file.Length / 1024 / 1024:F2}MB"
                    : file.Length > 1024
                        ? $"{(double)file.Length / 1024:F2}KB"
                        : (object)$"{(double)file.Length:F2}B";

                GifView.Visibility = Visibility.Visible;
                var image = new BitmapImage();
                image.BeginInit();
                var ms = new MemoryStream(File.ReadAllBytes(path));
                image.StreamSource = ms;
                image.EndInit();
                _currentGifPath = path;
                ImageBehavior.SetAnimatedSource(GifView, image);
            });
        }

        private void StopRecording()
        {
            _canChangeRegion = true;
            Recording = false;
            RecordButton.Content = "开始录制";
            RecordButton.Background = Brushes.White;
            RegionSelectButton.IsEnabled = true;
            GifScaleInteger.IsEnabled = true;
            GifFrameInteger.IsEnabled = true;
            RecordCursorCheckBox.IsEnabled = true;
            MemoryRecordCheckBox.IsEnabled = true;
            DXGIRecordCheckBox.IsEnabled = true;
            _recordingCancellationTokenSource?.Cancel();
        }

        private async Task OpenRegionSelectWindowAsync()
        {
            var regionSelect = RegionSelect.Begin();
            var (confirm, region) = await regionSelect.WaitForResultAsync().ConfigureAwait(false);
            _canChangeRegion = true;
            if (confirm) Region = region;

            Dispatcher.Invoke(() =>
            {
                if (Region != default)
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
                HotkeyManager.Current.AddOrReplace("PushRecordGif", Key.A, ModifierKeys.Control | ModifierKeys.Shift,
                    OnHotKey_PushRecordGif);
            }
            catch
            {
                success1 = false;
            }

            try
            {
                HotkeyManager.Current.AddOrReplace("SelectRegion", Key.S, ModifierKeys.Control | ModifierKeys.Shift,
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
                sb.AppendLine("Ctrl + Shift + A：开始/停止录制");
            if (!success2)
                sb.AppendLine("Ctrl + Shift + S：选择录制区域");
            Task.Run(() =>
            {
                MessageBox.Show(sb.ToString(),
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }).ConfigureAwait(false);
        }

        private void RemoveHotKeys()
        {
            HotkeyManager.Current.Remove("PushRecordGif");
            HotkeyManager.Current.Remove("SelectRegion");
        }

        private void OnHotKey_PushRecordGif(object sender, HotkeyEventArgs e)
        {
            if (!_canBeginRecord) return;
            RecordButton_Click(null, null);
        }

        private void OnHotKey_SelectRegion(object sender, HotkeyEventArgs e)
        {
            if (!_canChangeRegion) return;
            RegionSelectButton_Click(null, null);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (CheckOsVersion())
            {
                SetDefaultConfig();
                RegisterHotKeys();
                _canBeginRecord = false;
                _canChangeRegion = true;
#if !DEBUG
                Task.Run(Updater.CheckUpdate).ConfigureAwait(false);
#endif
            }
            else
            {
                var result = MessageBox.Show("本程序目前必须在 Windows 10 及以上版本才能运行", "错误", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                if (result == MessageBoxResult.OK) Close();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveConfig();
            AboutWindow.CloseInstance();
            RemoveHotKeys();
            _recordingCancellationTokenSource?.Cancel();
            _processingCancellationTokenSource?.Cancel();
            _canBeginRecord = false;
            _canChangeRegion = false;
            foreach (var file in _tempFileList.Where(File.Exists).ToArray())
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // ignored
                }
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_canBeginRecord) return;
            if (Recording)
                StopRecording();
            else
                StartRecording();
        }

        private void RegionSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_canChangeRegion) return;
            _canBeginRecord = false;
            _canChangeRegion = false;
            OpenRegionSelectWindowAsync().ConfigureAwait(false);
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow.Begin();
        }

        private void GifScaleInteger_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            Scale = (int)e.NewValue;
        }

        private void GifFrameInteger_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            Fps = (int)e.NewValue;
        }

        private void RecordCursorCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            RecordCursor = true;
        }

        private void RecordCursorCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            RecordCursor = false;
        }

        private void MemoryRecordCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            RecordInMemory = true;
        }

        private void MemoryRecordCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            RecordInMemory = false;
        }

        private void DXGIRecordCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            DXGIRecord = true;
        }

        private void DXGIRecordCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            DXGIRecord = false;
        }

        private void GifView_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var data = new MemoryStream(_lastRecordGifClipboardBytes);
            Clipboard.SetData("Html Format", data);
        }

        private void GifView_OnPreviewMouseLeftButtonDownPreviewMouseLeftButtonDown(object sender,
            MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentGifPath)) return;
            if (!(sender is Image gif)) return;
            var dataObject = new DataObject(DataFormats.FileDrop, new[] { _currentGifPath });
            DragDrop.DoDragDrop(gif, dataObject, DragDropEffects.Copy);
        }

        private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
        {
            ScreenInfo.ClearCache();
        }

        private static string GenerateTempFileName(string ext)
        {
            return Path.Combine(Path.GetTempPath(),
                    string.Join(string.Empty, Guid.NewGuid().ToByteArray().Select(x => x.ToString("X2"))) + ext)
                .Replace('\\', '/');
        }
    }
}