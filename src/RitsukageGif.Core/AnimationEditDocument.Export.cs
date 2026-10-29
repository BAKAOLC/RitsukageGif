using ImageMagick;

namespace RitsukageGif.Core
{
    // Instances are created by Open/Create in the main partial declaration.
    // ReSharper disable once ClassCannotBeInstantiated
    public sealed partial class AnimationEditDocument
    {
        // ReSharper disable once UnusedMember.Global
        public Task ExportAsync(string path, OutputFormat format, GifQuality quality = GifQuality.Balanced,
            CancellationToken token = default)
        {
            return ExportAsync(path, new()
            {
                Format = format switch
                {
                    OutputFormat.Gif => "gif", OutputFormat.WebP => "webp", OutputFormat.APng => "apng",
                    _ => throw new ArgumentOutOfRangeException(nameof(format)),
                },
                Dither = quality != GifQuality.Fast,
                Quality = quality == GifQuality.Fast ? 75 : 95,
                Overwrite = true,
            }, token);
        }

        public async Task ExportAsync(string path, MediaProcessOptions options, CancellationToken token = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(options);
            var format = (options.Format ?? Path.GetExtension(path).TrimStart('.')).ToLowerInvariant();
            if (format is not ("gif" or "webp" or "apng") || options.Quality is < 1 or > 100 ||
                options.Colors is < 2 or > 256)
                throw new ArgumentOutOfRangeException(nameof(options));
            Logger.ZLogInformation(
                $"Animation document export started: format={format}, width={Width}, height={Height}, frames={Frames.Count}");
            var destination = Path.GetFullPath(path);
            if (!options.Overwrite && File.Exists(destination)) throw new IOException(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await Task.Run(() => WriteFrames(temporary, format, options, token), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, options.Overwrite);
                Logger.ZLogInformation($"Animation document export completed: format={format}, frames={Frames.Count}");
            }
            catch (Exception error) when (token.IsCancellationRequested &&
                                          error is MagickException or ObjectDisposedException)
            {
                Logger.ZLogDebug($"Animation document export canceled during native encoding");
                throw new OperationCanceledException(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"Animation document export canceled");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"Animation document export failed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public void ExportFrame(int index, string path, CancellationToken token = default)
        {
            Select([index]);
            var destination = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                token.ThrowIfCancellationRequested();
                File.Copy(Frames[index].Path, temporary);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private void WriteFrames(string path, string format, MediaProcessOptions options, CancellationToken token)
        {
            if (format == "webp" && (Width > 16383 || Height > 16383))
                throw new ArgumentException(UiText.Get("RecordingTooLarge"));
            using var stream = File.Create(path);
            using var gif = format == "gif" ? new GifWriter(stream, Width, Height, true, Iterations) : null;
            var webp = format == "webp" ? new WebpAnimationWriter(stream, Width, Height, Iterations) : null;
            using var png = format == "apng" ? new StreamingPngWriter(stream, Width, Height, token, Iterations) : null;
            var bounds = new PixelRect(0, 0, Width, Height);
            long elapsedMs = 0, writtenCentiseconds = 0;
            foreach (var frame in Frames)
            {
                token.ThrowIfCancellationRequested();
                using var image = new MagickImage(frame.Path);
                image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                if (gif is not null)
                {
                    image.Quantize(new QuantizeSettings
                    {
                        Colors = (uint)options.Colors,
                        DitherMethod = options.Dither ? DitherMethod.FloydSteinberg : DitherMethod.No,
                    });
                    elapsedMs += frame.DelayMs;
                    // GIF stores hundredths of a second; retain every edited frame and quantize cumulative time.
                    var delay = checked((int)Math.Max(1, (elapsedMs + 5) / 10 - writtenCentiseconds));
                    writtenCentiseconds += delay;
                    gif.Add(image.ToByteArray(MagickFormat.Gif), bounds, delay);
                }
                else if (png is not null)
                {
                    using var pixels = image.GetPixels();
                    png.Add(pixels.ToByteArray(PixelMapping.BGRA)!, bounds, frame.DelayMs);
                }
                else
                {
                    image.Quality = (uint)options.Quality;
                    image.Settings.SetDefine(MagickFormat.WebP, "lossless", options.Lossless);
                    image.Settings.SetDefine(MagickFormat.WebP, "exact", true);
                    image.Settings.SetDefine(MagickFormat.WebP, "method", 3);
                    webp!.Add(image.ToByteArray(MagickFormat.WebP), bounds, frame.DelayMs);
                }
            }

            webp?.Complete();
            png?.Complete();
        }
    }
}
