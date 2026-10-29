namespace RitsukageGif.Core
{
    public static partial class HdrImageService
    {
        private static readonly SemaphoreSlim Gate = new(2);
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Image");

        public static async Task<byte[]> EncodePngAsync(CapturedFrame frame, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(frame);
            ValidateFrame(frame);
            await Gate.WaitAsync(token).ConfigureAwait(false);
            Logger.ZLogDebug(
                $"HDR PNG encoding started: width={frame.Width}, height={frame.Height}, alphaMode={frame.AlphaMode.ToString()}");
            try
            {
                var result = await Task.Run(() =>
                {
                    using var stream = new MemoryStream();
                    WritePng(stream, frame, token);
                    token.ThrowIfCancellationRequested();
                    return stream.ToArray();
                }, token).ConfigureAwait(false);
                Logger.ZLogDebug(
                    $"HDR PNG encoding completed: width={frame.Width}, height={frame.Height}, bytes={result.Length}");
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"HDR PNG encoding canceled");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"HDR PNG encoding failed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static void ValidateFrame(CapturedFrame frame)
        {
            if (frame.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb ||
                frame.Width <= 0 || frame.Height <= 0 ||
                frame.Stride < (long)frame.Width * 8 || (long)frame.Stride * frame.Height > 512L * 1024 * 1024 ||
                frame.Pixels.Length < (long)frame.Stride * frame.Height || !Enum.IsDefined(frame.AlphaMode))
                throw new ArgumentException(null, nameof(frame));
        }

        public static async Task<MediaInfo> SaveAsync(CapturedFrame frame, string output, HdrImageSaveOptions options,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(frame);
            ArgumentNullException.ThrowIfNull(options);
            ValidateFrame(frame);
            var format = options.Format ?? Path.GetExtension(output).ToLowerInvariant() switch
            {
                ".png" => HdrImageFormat.Png,
                ".exr" => HdrImageFormat.Exr,
                _ => throw new ArgumentOutOfRangeException(nameof(output)),
            };
            if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(options));
            output = Path.GetFullPath(output);
            await Gate.WaitAsync(token).ConfigureAwait(false);
            Logger.ZLogDebug(
                $"HDR image output started: width={frame.Width}, height={frame.Height}, format={format.ToString()}");
            try
            {
                var result = await Task.Run(() => Save(frame, output, options.Overwrite, format, token), token)
                    .ConfigureAwait(false);
                Logger.ZLogInformation(
                    $"HDR image output completed: output={result.Path}, format={format.ToString()}, width={result.Width}, height={result.Height}, bytes={result.Bytes}");
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"HDR image output canceled");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"HDR image output failed: exceptionType={error.GetType().Name}, hResult={error.HResult}");
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static MediaInfo Save(CapturedFrame frame, string output, bool overwrite, HdrImageFormat format,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!overwrite && File.Exists(output)) throw new IOException(output);
            var directory = Path.GetDirectoryName(output)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            try
            {
                long size;
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           65536, FileOptions.SequentialScan))
                {
                    if (format == HdrImageFormat.Png) WritePng(stream, frame, token);
                    else WriteExr(stream, frame, token);
                    size = stream.Position;
                }

                token.ThrowIfCancellationRequested();
                File.Move(temporary, output, overwrite);
                return new(output, size, frame.Width, frame.Height, 1, 0, format.ToString().ToLowerInvariant());
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
