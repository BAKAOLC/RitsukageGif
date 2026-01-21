using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = System.Windows.Controls.Image;

namespace RitsukageGif.Controls
{
    public class AnimatedImageControl : Image
    {
        public static readonly DependencyProperty SourcePathProperty =
            DependencyProperty.Register(nameof(SourcePath), typeof(string), typeof(AnimatedImageControl),
                new(null, OnSourcePathChanged));

        private BitmapImage[] _cachedFrames;

        private CancellationTokenSource _cancellationTokenSource;
        private int _currentFrame;
        private int[] _frameDelays;
        private Image<Rgba32> _image;
        private bool _isPlaying;

        public string SourcePath
        {
            get => (string)GetValue(SourcePathProperty);
            set => SetValue(SourcePathProperty, value);
        }

        private static void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AnimatedImageControl control)
                _ = control.LoadAndPlayAnimation((string)e.NewValue);
        }

        private async Task LoadAndPlayAnimation(string path)
        {
            StopAnimation();
            ClearCache();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                UpdateSource(null);
                return;
            }

            try
            {
                _image?.Dispose();
                _image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(path).ConfigureAwait(false);

                if (_image.Frames.Count > 1)
                {
                    await CacheAllFrames().ConfigureAwait(false);
                    StartAnimation();
                }
                else
                {
                    await CacheAllFrames().ConfigureAwait(false);
                    ShowFrame(0);
                }
            }
            catch
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new(path, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        Source = bitmap;
                    }
                    catch
                    {
                        Source = null;
                    }
                });
            }
        }

        private async Task CacheAllFrames()
        {
            if (_image == null) return;

            await Task.Run(() =>
            {
                var frameCount = _image.Frames.Count;
                _cachedFrames = new BitmapImage[frameCount];
                _frameDelays = new int[frameCount];

                for (var i = 0; i < frameCount; i++)
                {
                    var frame = _image.Frames[i];
                    _frameDelays[i] = GetFrameDelay(frame);

                    try
                    {
                        using var memoryStream = new MemoryStream();
                        using (var singleFrameImage = new Image<Rgba32>(frame.Width, frame.Height))
                        {
                            frame.ProcessPixelRows(singleFrameImage.Frames.RootFrame, (source, target) =>
                            {
                                for (var j = 0; j < source.Height; j++)
                                    source.GetRowSpan(j).CopyTo(target.GetRowSpan(j));
                            });
                            singleFrameImage.SaveAsPng(memoryStream);
                        }

                        memoryStream.Position = 0;

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = memoryStream;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        _cachedFrames[i] = bitmap;
                    }
                    catch
                    {
                        _cachedFrames[i] = null;
                    }
                }
            }).ConfigureAwait(false);
        }

        private void ClearCache()
        {
            _cachedFrames = null;
            _frameDelays = null;
        }

        private void StartAnimation()
        {
            if (_isPlaying) return;

            _isPlaying = true;
            _currentFrame = 0;
            _cancellationTokenSource = new();

            Task.Run(() => AnimationLoop(_cancellationTokenSource.Token));
        }

        private void UpdateSource(BitmapImage bitmap)
        {
            Application.Current.Dispatcher.Invoke(() => { Source = bitmap; });
        }

        private async Task AnimationLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _cachedFrames != null)
                try
                {
                    var delay = _frameDelays[_currentFrame];

                    ShowFrame(_currentFrame);

                    _currentFrame = (_currentFrame + 1) % _cachedFrames.Length;

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }
        }

        private static int GetFrameDelay(ImageFrame<Rgba32> frame)
        {
            var gifMetadata = frame.Metadata.GetGifMetadata();
            if (gifMetadata.FrameDelay > 0) return gifMetadata.FrameDelay * 10;

            var pngMetadata = frame.Metadata.GetPngMetadata();
            {
                var frameDelay = pngMetadata.FrameDelay;
                if (frameDelay.Denominator > 0) return (int)(frameDelay.Numerator * 1000.0 / frameDelay.Denominator);
            }

            var webpMetadata = frame.Metadata.GetWebpMetadata();
            if (webpMetadata.FrameDelay > 0) return (int)webpMetadata.FrameDelay;

            return 100;
        }

        private void ShowFrame(int frameIndex)
        {
            if (_cachedFrames == null || frameIndex >= _cachedFrames.Length)
                return;

            var bitmap = _cachedFrames[frameIndex];
            if (bitmap != null)
                UpdateSource(bitmap);
        }

        private void StopAnimation()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _isPlaying = false;
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            });
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Unloaded += (s, args) =>
            {
                StopAnimation();
                ClearCache();
                _image?.Dispose();
                _image = null;
            };
        }
    }
}