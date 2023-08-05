using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;

namespace RitsukageGif.Windows
{
    /// <summary>
    ///     AboutWindow.xaml 的交互逻辑
    /// </summary>
    public partial class AboutWindow : Window
    {
        private static AboutWindow _instance;

        private static readonly string AboutHeaderText =
            $@"RitsukageGif ver {Assembly.GetExecutingAssembly().GetName().Version}
构建日期: {File.GetLastWriteTime(typeof(AboutWindow).Assembly.Location)}

{AboutText}";

        private bool _closeFlag;

        public AboutWindow()
        {
            _instance = this;
            InitializeComponent();
        }

        private static string AboutText
        {
            get
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RitsukageGif.About.txt"))
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AboutTextBox.Text = AboutHeaderText;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_closeFlag) return;
            e.Cancel = true;
            AboutTextBox.ScrollToHome();
            Hide();
        }

        private static AboutWindow GetInstance()
        {
            return _instance ?? (_instance = new AboutWindow());
        }

        public static AboutWindow Begin()
        {
            var instance = GetInstance();
            instance.Show();
            return instance;
        }

        public static void CloseInstance()
        {
            if (_instance == null) return;
            _instance._closeFlag = true;
            _instance.Close();
            _instance = null;
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}