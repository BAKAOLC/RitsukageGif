using System;
using System.Windows;
using DRectangle = System.Drawing.Rectangle;

namespace CaptureGif
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public SelectedRegionResult Region { get; private set; }

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
            if (confirm)
            {
                if (region != default)
                {
                    Region = region;
                    MessageBox.Show($"Selected region: {Environment.NewLine}{region}");
                }
                else
                {
                    Region = null;
                    MessageBox.Show("No region selected");
                }
            }
        }

        private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
        {
        }
    }
}
