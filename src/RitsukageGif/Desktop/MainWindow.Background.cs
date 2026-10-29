using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    public sealed partial class MainWindow
    {
        private Bitmap? _background;
        private CancellationTokenSource? _backgroundCancellation;
        private bool _backgroundDisposed;

        private void ApplyBackground()
        {
            if (_backgroundDisposed) return;
            _backgroundCancellation?.Cancel();
            _backgroundCancellation = null;
            if (!Settings.ShowBackground)
            {
                ReplaceBackground(null);
                return;
            }

            var cancellation = new CancellationTokenSource();
            _backgroundCancellation = cancellation;
            _ = LoadBackgroundAsync(Settings.BackgroundImage, cancellation);
        }

        private async Task LoadBackgroundAsync(string location, CancellationTokenSource cancellation)
        {
            Bitmap? replacement = null;
            string? error = null;
            try
            {
                try
                {
                    replacement = await Task.Run(async () =>
                    {
                        if (string.IsNullOrWhiteSpace(location)) return LoadOriginalBackground();
                        using var resource = await MediaResource.OpenAsync(location, cancellation.Token)
                            .ConfigureAwait(false);
                        cancellation.Token.ThrowIfCancellationRequested();
                        return DecodeBackground(resource.Path);
                    }, cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    error = UiText.Get("BackgroundFailed") + exception.Message;
                    if (!cancellation.IsCancellationRequested)
                        replacement = await Task.Run(LoadOriginalBackground, cancellation.Token).ConfigureAwait(false);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_backgroundDisposed || cancellation.IsCancellationRequested ||
                        !ReferenceEquals(_backgroundCancellation, cancellation)) return;
                    ReplaceBackground(replacement);
                    replacement = null;
                    if (error is not null) _host.SetStatus(error);
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Replacing the background or closing the window cancels pending work.
            }
            catch (Exception exception)
            {
                Trace.TraceError("Background loading failed: {0}", exception);
            }
            finally
            {
                replacement?.Dispose();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ReferenceEquals(_backgroundCancellation, cancellation)) _backgroundCancellation = null;
                    cancellation.Dispose();
                });
            }
        }

        private static Bitmap LoadOriginalBackground()
        {
            using var stream = AssetLoader.Open(new("avares://RitsukageGif/Background.png"));
            return new(stream);
        }

        private static Bitmap DecodeBackground(string path)
        {
            try
            {
                return new(path);
            }
            catch (ArgumentException)
            {
                // TIFF and other original WPF image formats may need the shared image decoder.
                using var decoded = new MagickImage(path);
                using var converted = new MemoryStream();
                decoded.Write(converted, MagickFormat.Png);
                converted.Position = 0;
                return new(converted);
            }
        }

        private void ReplaceBackground(Bitmap? replacement)
        {
            _backgroundView.Source = replacement;
            _background?.Dispose();
            _background = replacement;
        }

        private void DisposeBackground()
        {
            _backgroundDisposed = true;
            _backgroundCancellation?.Cancel();
            _backgroundCancellation = null;
            ReplaceBackground(null);
        }
    }
}
