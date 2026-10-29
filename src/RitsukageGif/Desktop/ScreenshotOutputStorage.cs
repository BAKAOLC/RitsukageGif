using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal static class ScreenshotOutputStorage
    {
        private static readonly SemaphoreSlim Gate = new(2);

        public static Task<string?> PickPathAsync(Window? owner, IStorageProvider storage,
            ScreenshotSaveProfile preferences, string? outputDirectory, string titleKey,
            OutputColorMode colorMode, AppearanceDialogStyle style, ScreenshotNameContext? context = null,
            CancellationToken token = default)
        {
            if (style == AppearanceDialogStyle.Modern)
                return ScreenshotSaveDialog.PickAsync(owner, storage, preferences.Normalize(),
                    outputDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    titleKey, colorMode, context ?? new(DateTime.Now), token);
            return PickSystemPathAsync(storage, preferences, outputDirectory, titleKey, colorMode, context, token);
        }

        // ReSharper disable once UnusedMember.Global
        public static async Task<string?> PickPathAsync(IStorageProvider storage,
            ScreenshotSaveProfile preferences, string? outputDirectory, string titleKey,
            OutputColorMode colorMode, CancellationToken token = default)
        {
            return await PickSystemPathAsync(storage, preferences, outputDirectory, titleKey, colorMode, null, token);
        }

        private static async Task<string?> PickSystemPathAsync(IStorageProvider storage,
            ScreenshotSaveProfile preferences, string? outputDirectory, string titleKey,
            OutputColorMode colorMode, ScreenshotNameContext? context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var normalized = preferences.Normalize();
            var format = colorMode == OutputColorMode.Sdr ? normalized.Format : StaticImageFormat.Png;
            var extension = normalized.DialogExtension(colorMode);
            var name = Path.ChangeExtension(normalized.CreateFileName(context ?? new(DateTime.Now), format), extension);
            FilePickerFileType[] formats =
            [
                new("PNG") { Patterns = ["*.png"] }, new("JPEG") { Patterns = ["*.jpg", "*.jpeg"] },
                new("BMP") { Patterns = ["*.bmp"] }, new("WebP") { Patterns = ["*.webp"] },
            ];
            var choices = colorMode switch
            {
                OutputColorMode.Sdr => formats,
                OutputColorMode.Hdr =>
                    [new("HDR PNG") { Patterns = ["*.png"] }, new("OpenEXR") { Patterns = ["*.exr"] }],
                OutputColorMode.Both => [new("PNG") { Patterns = ["*.png"] }],
                _ => throw new ArgumentOutOfRangeException(nameof(colorMode)),
            };
            if (!string.IsNullOrWhiteSpace(normalized.Directory)) outputDirectory = normalized.Directory;
            var directory = string.IsNullOrWhiteSpace(outputDirectory)
                ? null
                : await storage.TryGetFolderFromPathAsync(outputDirectory);
            token.ThrowIfCancellationRequested();
            var file = await storage.SaveFilePickerAsync(new()
            {
                Title = UiText.Get(titleKey), SuggestedFileName = name,
                DefaultExtension = Path.GetExtension(name).TrimStart('.'),
                ShowOverwritePrompt = colorMode == OutputColorMode.Sdr &&
                                      normalized.ConflictPolicy == ScreenshotConflictPolicy.Overwrite,
                SuggestedStartLocation = directory,
                SuggestedFileType =
                    choices.FirstOrDefault(choice => choice.Patterns?.Contains("*" + extension) == true),
                FileTypeChoices = choices,
            });
            token.ThrowIfCancellationRequested();
            return file?.TryGetLocalPath();
        }

        public static async Task<MediaInfo> SaveAsync(ReadOnlyMemory<byte> png, string path,
            ScreenshotSaveProfile preferences, CancellationToken token = default)
        {
            if (png.IsEmpty || png.Length > PinnedImageContent.MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(png));
            var normalized = preferences.Normalize();
            path = Path.GetFullPath(path);
            var format = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => StaticImageFormat.Png,
                ".jpg" or ".jpeg" => StaticImageFormat.Jpeg,
                ".bmp" => StaticImageFormat.Bmp,
                ".webp" => StaticImageFormat.WebP,
                _ => throw new ArgumentOutOfRangeException(nameof(path)),
            };
            await Gate.WaitAsync(token).ConfigureAwait(false);
            string? temporary = null;
            try
            {
                token.ThrowIfCancellationRequested();
                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);
                temporary = Path.Combine(directory, $".{Guid.NewGuid():N}{Path.GetExtension(path)}");
                MediaInfo info;
                if (format == StaticImageFormat.Png)
                {
                    var dimensions = await Task.Run(() => ReadPngDimensions(png, token), token).ConfigureAwait(false);
                    await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                                     FileShare.None, 81920, FileOptions.Asynchronous))
                        await stream.WriteAsync(png, token).ConfigureAwait(false);
                    info = new(temporary, png.Length, dimensions.Width, dimensions.Height, 1, 0, "png");
                }
                else
                {
                    var decoded = await Task.Run(() => PinnedImageContent.Decode(png, token), token)
                        .ConfigureAwait(false);
                    info = await new StaticImageService().SaveAsync(decoded.SdrPng ?? png,
                        temporary,
                        normalized.CreateOptions(format) with { Overwrite = false }, token).ConfigureAwait(false);
                }

                for (;;)
                {
                    token.ThrowIfCancellationRequested();
                    var destination = normalized.ResolvePath(path);
                    try
                    {
                        File.Move(temporary, destination,
                            normalized.ConflictPolicy == ScreenshotConflictPolicy.Overwrite);
                        return info with { Path = destination };
                    }
                    catch (IOException) when (normalized.ConflictPolicy == ScreenshotConflictPolicy.Rename &&
                                              Path.Exists(destination))
                    {
                    }
                }
            }
            finally
            {
                try
                {
                    if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
                }
                finally
                {
                    Gate.Release();
                }
            }
        }

        private static (int Width, int Height) ReadPngDimensions(ReadOnlyMemory<byte> png, CancellationToken token)
        {
            using var image = new MagickImage();
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            var settings = new MagickReadSettings { FrameCount = 1 };
            try
            {
                image.Ping(png.Span, settings);
                if (image.Format != MagickFormat.Png || image.Width == 0 || image.Height == 0 ||
                    (long)image.Width * image.Height > 128L * 1024 * 1024)
                    throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(png));
                token.ThrowIfCancellationRequested();
                image.Read(png.Span, settings);
                token.ThrowIfCancellationRequested();
                return (checked((int)image.Width), checked((int)image.Height));
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        }
    }
}
