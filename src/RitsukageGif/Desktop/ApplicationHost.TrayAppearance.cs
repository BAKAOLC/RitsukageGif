using Avalonia.Platform;
using ImageMagick;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private TrayIconAppearance? _trayAppearance;
        private uint _trayColor;

        private void RefreshTrayAppearance()
        {
            if (_tray is null) return;
            var mode = Settings.Appearance.TrayIcon == TrayIconAppearance.Automatic
                ? TrayIconAppearance.Original
                : Settings.Appearance.TrayIcon;
            var color = mode == TrayIconAppearance.Monochrome ? Settings.Appearance.TrayIconColor : 0;
            if (_trayAppearance == mode && _trayColor == color) return;
            if (mode == TrayIconAppearance.Hidden)
            {
                if (View is not { IsVisible: true } && !IsClosing) ShowMainWindow();
                _tray.IsVisible = false;
            }
            else
            {
                using var stream = AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
                if (mode == TrayIconAppearance.Original)
                {
                    _tray.Icon = new(stream);
                }
                else
                {
                    using var decoded = new MagickImage(stream, MagickFormat.Ico);
                    decoded.Resize(32, 32);
                    using var bitmap = SKBitmap.Decode(decoded.ToByteArray(MagickFormat.Png));
                    using var surface = SKSurface.Create(new SKImageInfo(32, 32));
                    using var filter = SKColorFilter.CreateBlendMode(new(color), SKBlendMode.SrcIn);
                    using var paint = new SKPaint();
                    paint.ColorFilter = filter;
                    paint.IsAntialias = true;
                    surface.Canvas.Clear(SKColors.Transparent);
                    surface.Canvas.DrawBitmap(bitmap, new SKRect(0, 0, 32, 32), paint);
                    using var image = surface.Snapshot();
                    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var icon = encoded.AsStream();
                    _tray.Icon = new(icon);
                }

                _tray.IsVisible = true;
            }

            _trayAppearance = mode;
            _trayColor = color;
        }
    }
}
