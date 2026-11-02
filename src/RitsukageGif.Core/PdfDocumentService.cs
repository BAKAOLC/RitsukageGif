using ImageMagick;
using SkiaSharp;

namespace RitsukageGif.Core
{
    public sealed partial class PdfDocumentService : IDocumentOutputService
    {
        private static readonly SemaphoreSlim Gate = new(1);
        private static readonly ILogger Logger = LogHub.GetLogger("Core.Document");

        public Task<DocumentOutputInfo> SaveAsync(IReadOnlyList<string> images, string output,
            DocumentOutputOptions options, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(images);
            if (images.Count is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(images));
            return SaveCoreAsync(images.Count, index => Decode(images[index], token), output, options, token);
        }

        public Task<DocumentOutputInfo> SaveImageAsync(ReadOnlyMemory<byte> image, string output,
            DocumentOutputOptions options, CancellationToken token = default)
        {
            if (image.IsEmpty) throw new ArgumentException(null, nameof(image));
            return SaveCoreAsync(1, _ => Decode(image, token), output, options, token);
        }

        private static async Task<DocumentOutputInfo> SaveCoreAsync(int count, Func<int, SKBitmap> decode,
            string output, DocumentOutputOptions options, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!double.IsFinite(options.Dpi) || options.Dpi <= 0 || options.Dpi > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(options));
            output = Path.GetFullPath(output);
            if (!string.Equals(Path.GetExtension(output), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Document output must use the .pdf extension.", nameof(output));
            if (!SKColor.TryParse(options.Background, out var background))
                throw new ArgumentException("Invalid PDF page background color.", nameof(options));
            if (!options.Overwrite && File.Exists(output)) throw new IOException(output);
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => Write(count, decode, output, options, background, token), token)
                    .ConfigureAwait(false);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"PDF output canceled during native processing");
                throw new OperationCanceledException(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.ZLogDebug($"PDF output canceled");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error,
                    $"PDF output failed. ExceptionType={error.GetType().Name} HResult={error.HResult}");
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static DocumentOutputInfo Write(int count, Func<int, SKBitmap> decode, string output,
            DocumentOutputOptions options, SKColor background, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(output)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
            var pages = 0;
            Logger.ZLogInformation(
                $"PDF output started. Images={count} Paper={options.PaperSize.ToString()} PageMode={options.PageMode.ToString()}");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
                       {
                           RasterDpi = (float)options.Dpi,
                           EncodingQuality = options.Lossless ? 101 : options.Quality,
                       }) ?? throw new InvalidOperationException("PDF output is unavailable."))
                using (var paint = new SKPaint())
                    try
                    {
                        for (var index = 0; index < count; index++)
                        {
                            token.ThrowIfCancellationRequested();
                            using var bitmap = decode(index);
                            using var image = SKImage.FromBitmap(bitmap);
                            var layouts = PdfDocumentLayout.Create(bitmap.Width, bitmap.Height, options);
                            if (pages + layouts.Count > 4096)
                                throw new InvalidOperationException("PDF output exceeds the 4096-page limit.");
                            foreach (var layout in layouts)
                            {
                                token.ThrowIfCancellationRequested();
                                var canvas = document.BeginPage((float)layout.WidthPoints, (float)layout.HeightPoints);
                                canvas.Clear(background);
                                var source = SKRect.Create((float)layout.SourceX, (float)layout.SourceY,
                                    (float)layout.SourceWidth, (float)layout.SourceHeight);
                                var destination = SKRect.Create((float)layout.DestinationX, (float)layout.DestinationY,
                                    (float)layout.DestinationWidth, (float)layout.DestinationHeight);
                                canvas.DrawImage(image, source, destination, new(SKCubicResampler.Mitchell), paint);
                                document.EndPage();
                                pages++;
                            }
                        }

                        token.ThrowIfCancellationRequested();
                        document.Close();
                    }
                    catch
                    {
                        document.Abort();
                        throw;
                    }

                token.ThrowIfCancellationRequested();
                var info = new DocumentOutputInfo(output, new FileInfo(temporary).Length, pages);
                File.Move(temporary, output, options.Overwrite);
                Logger.ZLogInformation($"PDF output completed. Pages={pages} Bytes={info.Bytes}");
                return info;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
