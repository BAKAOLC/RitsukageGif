using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace CaptureGif
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public SelectedRegionResult Region { get; private set; }

        private int _delay;

        private int _fps;
        public int FPS
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

        public bool Recording { get; private set; }

        public Gif.RecordInfo RecordInfo { get; private set; }

        private CancellationTokenSource _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            FPS = 30;
        }

        private async void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (Recording)
            {
                Recording = false;
                RecordButton.Content = "开始录制";
                RecordButton.Background = Brushes.White;
                GifScaleInterger.IsEnabled = true;
                GifFrameInterger.IsEnabled = true;
                RecordCursorCheckBox.IsEnabled = true;
                _cancellationTokenSource?.Cancel();
            }
            else
            {
                Recording = true;
                RecordButton.Content = "停止录制";
                RecordButton.Background = Brushes.Red;
                GifScaleInterger.IsEnabled = false;
                GifFrameInterger.IsEnabled = false;
                RecordCursorCheckBox.IsEnabled = false;
                _cancellationTokenSource = new CancellationTokenSource();
                Gif.RecordInfo info = null;
                Task task1 = Task.Run(() =>
                {
                    Gif.Begin(Path.GetTempFileName(), Region.Converted,
                        _delay, Scale, RecordCursor, _cancellationTokenSource.Token, out info);
                    RecordInfo = info;
                }, _cancellationTokenSource.Token);
                Task task2 = Task.Run(() =>
                {
                    do
                    {
                        Thread.Sleep(30);
                        Dispatcher.Invoke(() =>
                        {
                        });
                    }
                    while (!(info != null && info.Completed));
                    var file = new FileInfo(info.Path);
                    Dispatcher.Invoke(() =>
                    {
                        if (file.Length > 1024 * 1024)
                            GifSizeLable.Content = $"文件大小：{(double)file.Length / 1024 / 1024:F2}MB";
                        else if (file.Length > 1024)
                            GifSizeLable.Content = $"文件大小：{(double)file.Length / 1024:F2}KB";
                        else
                            GifSizeLable.Content = $"文件大小：{(double)file.Length:F2}B";
                    });
                    string path = file.FullName.Replace("\\", "/");
                    var sb = new StringBuilder();
                    sb.Append("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">");
                    sb.AppendFormat("<html><body><!--StartFragment--><p><img src=\"file:///{0}\"></p><!--EndFragment--></body></html>", path);
                    var data = new MemoryStream(Encoding.Default.GetBytes(sb.ToString()));
                    Clipboard.SetData("Html Format", data);
                }, _cancellationTokenSource.Token);
            }
        }

        private async void RegionSelectButton_Click(object sender, RoutedEventArgs e)
        {
            var regionSelect = RegionSelect.Begin();
            (var confirm, var region) = await regionSelect.WaitForResult().ConfigureAwait(false);
            if (confirm)
            {
                if (region != default)
                {
                    Region = region;
                }
                else
                {
                    Region = null;
                }
            }
            Dispatcher.Invoke(() =>
            {
                if (Region != default)
                {
                    RecordButton.IsEnabled = true;
                }
                else
                {
                    RecordButton.IsEnabled = false;
                }
            });
        }

        private void GifScaleInterger_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            Scale = (int)e.NewValue;
        }

        private void GifFrameInterger_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            FPS = (int)e.NewValue;
        }

        private void RecordCursorCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            RecordCursor = true;
        }

        private void RecordCursorCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            RecordCursor = false;
        }

        private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
        {
            ScreenInfo.ClearCache();
        }
    }
}
