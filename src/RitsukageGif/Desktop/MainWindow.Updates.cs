using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed partial class MainWindow
    {
        private readonly Button _updateAvailable = new()
        {
            Name = "AvailableUpdate", Content = UiText.Get("DownloadUpdate"), IsVisible = false,
            FontSize = 11, Padding = new(3, 0), MinHeight = 0, Background = Brushes.Transparent,
            BorderThickness = new(0), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        private void OpenAvailableUpdate()
        {
            if (_host.AvailableUpdate is not { } release) return;
            try
            {
                Process.Start(new ProcessStartInfo(release.DownloadUrl) { UseShellExecute = true });
            }
            catch (Exception)
            {
                _host.SetStatus(UiText.Get("UpdateOpenFailed"));
            }
        }
    }
}
