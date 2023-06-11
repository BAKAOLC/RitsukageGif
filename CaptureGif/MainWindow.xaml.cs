using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using DRectangle = System.Drawing.Rectangle;

namespace CaptureGif
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public DRectangle? Region { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        { }

        private async void RegionSelectButton_Click(object sender, RoutedEventArgs e)
        {
            var regionSelect = RegionSelect.Begin();
            (var confirm, var region) = await regionSelect.WaitForResult().ConfigureAwait(false);
            if (confirm && region != default)
            {
                Region = region;
                MessageBox.Show($"Selected region: {region}");
            }
        }

        private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
        {
        }
    }
}
