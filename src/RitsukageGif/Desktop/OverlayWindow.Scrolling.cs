using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayWindow
    {
        private void ConfigureScrollingPreview(Grid layers, PixelRect region, double scale)
        {
            Content = null;
            var area = Screens.Primary?.WorkingArea;
            var dpi = Screens.Primary?.Scaling ?? 1;
            Width = Math.Min(960, (area?.Width ?? 1280) / dpi * .9);
            Height = Math.Min(760, (area?.Height ?? 800) / dpi * .9);
            CanResize = true;
            MinWidth = Math.Min(480, Width);
            MinHeight = Math.Min(360, Height);
            layers.Width = region.Width / scale;
            layers.Height = region.Height / scale;
            layers.HorizontalAlignment = HorizontalAlignment.Left;
            layers.VerticalAlignment = VerticalAlignment.Top;
            var root = new Grid { RowDefinitions = new("Auto,*") };
            if (_screenshotToolbar is not null)
            {
                layers.Children.Remove(_screenshotToolbar);
                _screenshotToolbar.IsDocked = true;
                root.Children.Add(_screenshotToolbar);
            }

            var scroll = new ScrollViewer
            {
                Content = layers, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            Content = root;
            scroll.ScrollChanged += (_, _) => RefreshViewport();
            scroll.SizeChanged += (_, _) => RefreshViewport();
            Opened += (_, _) => RefreshViewport();
            return;

            void RefreshViewport()
            {
                _canvas.SetVisibleSource(new(scroll.Offset.X * scale,
                    scroll.Offset.Y * scale, scroll.Viewport.Width * scale, scroll.Viewport.Height * scale));
            }
        }
    }
}
