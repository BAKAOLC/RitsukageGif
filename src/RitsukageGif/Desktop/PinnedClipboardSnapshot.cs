using System.Text;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedClipboardSnapshot(
        IReadOnlyList<(string Format, byte[] Bytes)> Images,
        byte[]? Bitmap,
        string[] Files,
        string? Text,
        string? Html)
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Pins.ClipboardSnapshot");

        private List<(CaptureWindowShape? Shape, double CaptureScaling)>? ImageShapes { get; init; }

        internal IReadOnlyDictionary<string, (CaptureWindowShape? Shape, double CaptureScaling)>? FileShapes
        {
            get;
            init;
        }

        internal CaptureWindowShape? BitmapWindowShape { get; init; }
        internal double BitmapCaptureScaling { get; init; } = 1;

        public string? FormulaSource { get; private init; }
        public string? HtmlSourceUrl { get; private init; }
        public PinnedHtmlSelection? HtmlSelection { get; private init; }
        public string? SourceHtml { get; private init; } = Html;
        public string? SourceText { get; private init; } = Text;

        internal bool HasPowerPointGenerator => Html?.Contains(
            """<meta name=Generator content="Microsoft PowerPoint""", StringComparison.Ordinal) == true;

        internal (CaptureWindowShape? Shape, double CaptureScaling) ShapeForFile(string path)
        {
            return FileShapes is not null && FileShapes.TryGetValue(path, out var shape) ? shape : (null, 1);
        }

        internal (CaptureWindowShape? Shape, double CaptureScaling) ShapeForImage(int index)
        {
            return ImageShapes is not null && index < ImageShapes.Count ? ImageShapes[index] : (null, 1);
        }

        public static async Task<PinnedClipboardSnapshot> ReadEnabledAsync(IAsyncDataTransfer transfer,
            PinnedContentKinds kinds, CancellationToken token)
        {
            var images = new List<(string, byte[])>();
            List<(CaptureWindowShape? Shape, double CaptureScaling)>? imageShapes = null;
            Dictionary<IAsyncDataTransferItem, (CaptureWindowShape? Shape, double CaptureScaling)>? itemShapes = null;
            Dictionary<string, (CaptureWindowShape? Shape, double CaptureScaling)>? fileShapes = null;
            if ((kinds & PinnedContentKinds.Images) != 0)
                foreach (var item in transfer.Items)
                    if (item.Formats.Contains(ClipboardWindowShape.Format))
                    {
                        var raw = await ReadOptionalAsync(
                            async () => await item.TryGetRawAsync(ClipboardWindowShape.Format), token);
                        var shape = ClipboardWindowShape.Read(raw as byte[]);
                        if (shape.Shape is null && shape.CaptureScaling is 1) continue;
                        (itemShapes ??= [])[item] = shape;
                        if (item.Formats.Contains(DataFormat.File))
                        {
                            var file = await ReadOptionalAsync(async () => await item.TryGetFileAsync(), token);
                            if (file?.TryGetLocalPath() is { } path)
                                (fileShapes ??= new(OperatingSystem.IsWindows()
                                    ? StringComparer.OrdinalIgnoreCase
                                    : StringComparer.Ordinal))[path] = shape;
                        }
                    }

            if ((kinds & PinnedContentKinds.Images) != 0)
                foreach (var name in ClipboardPlatformFormats.ImageNames)
                {
                    var format = DataFormat.CreateBytesPlatformFormat(name);
                    foreach (var item in transfer.Items)
                    {
                        token.ThrowIfCancellationRequested();
                        if (item.Formats.Contains(format) &&
                            await ReadOptionalAsync(async () => await item.TryGetRawAsync(format), token) is byte[]
                            {
                                Length: > 0,
                            } data)
                        {
                            if (data.Length > PinnedImageContent.MaximumEncodedBytes)
                                continue;
                            images.Add((name, data));
                            if (itemShapes is not null)
                                (imageShapes ??= []).Add(itemShapes.TryGetValue(item, out var shape)
                                    ? shape
                                    : (null, 1));
                        }
                    }
                }

            var bitmapInput = images.Count == 0 && (kinds & PinnedContentKinds.Images) != 0
                ? await ReadOptionalAsync(() => ReadBitmapAsync(transfer, token), token)
                : null;

            var files = (kinds & (PinnedContentKinds.Files | PinnedContentKinds.Images)) != 0
                ? await ReadOptionalAsync(async () => await transfer.TryGetFilesAsync(), token)
                : null;
            var paths = files?.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            var text = kinds != PinnedContentKinds.None
                ? await ReadOptionalAsync(async () => await transfer.TryGetTextAsync(), token)
                : null;
            var sourceText = OperatingSystem.IsWindows() ? text?.Replace("\r\n", "\n") : text;
            string? html = null;
            string? sourceHtml = null;
            string? htmlSourceUrl = null;
            PinnedHtmlSelection? htmlSelection = null;
            if (kinds != PinnedContentKinds.None)
                foreach (var name in ClipboardPlatformFormats.HtmlNames)
                {
                    foreach (var item in transfer.Items)
                    {
                        var format = item.Formats.FirstOrDefault(candidate =>
                            candidate == DataFormat.CreateBytesPlatformFormat(name));
                        if (format is null) continue;
                        var raw = await ReadOptionalAsync(async () => await item.TryGetRawAsync(format), token);
                        try
                        {
                            if (raw is byte[] windowsHtml && name == "HTML Format")
                            {
                                (html, htmlSelection) = PinnedHtmlFragment.ReadWindows(windowsHtml);
                                sourceHtml = htmlSelection.HasContext ? html.Replace("\r", "") : null;
                            }
                            else
                            {
                                html = raw switch
                                {
                                    byte[] bytes => Encoding.UTF8.GetString(bytes).TrimEnd('\0'),
                                    string value => value,
                                    _ => null,
                                };
                                htmlSelection = html is null ? null : PinnedHtmlSelection.FromMarkers(html);
                                sourceHtml = html;
                            }

                            htmlSourceUrl = html is not null && raw is byte[] windowsBytes && name == "HTML Format"
                                ? PinnedHtmlFragment.SourceUrl(windowsBytes)
                                : null;
                        }
                        catch (Exception error) when (error is InvalidDataException or DecoderFallbackException)
                        {
                            Logger.ZLogWarning(
                                $"Clipboard HTML representation is invalid. ErrorType={error.GetType().Name}");
                            html = null;
                            sourceHtml = null;
                        }

                        if (html is not null) break;
                    }

                    if (html is not null) break;
                }

            string? formula = null;
            if ((kinds & PinnedContentKinds.Formula) != 0)
                foreach (var name in new[] { "application/x-latex", "text/x-tex" })
                {
                    var expected = ClipboardPlatformFormats.CreateBytesFormat(name);
                    foreach (var item in transfer.Items)
                    {
                        var format = item.Formats.FirstOrDefault(candidate => candidate == expected);
                        if (format is null) continue;
                        formula = await ReadOptionalAsync(async () => await item.TryGetRawAsync(format), token) switch
                        {
                            byte[] bytes => Encoding.UTF8.GetString(bytes).TrimEnd('\0'),
                            string value => value,
                            _ => null,
                        };
                        if (!string.IsNullOrWhiteSpace(formula)) break;
                    }

                    if (!string.IsNullOrWhiteSpace(formula)) break;
                }

            token.ThrowIfCancellationRequested();
            return new(images, bitmapInput?.Png, paths, text, html)
            {
                FormulaSource = formula, HtmlSourceUrl = htmlSourceUrl, HtmlSelection = htmlSelection,
                ImageShapes = imageShapes, FileShapes = fileShapes,
                BitmapWindowShape = bitmapInput?.WindowShape ?? default,
                BitmapCaptureScaling = bitmapInput?.CaptureScaling ?? 1,
                SourceHtml = sourceHtml, SourceText = sourceText,
            };
        }

        private static async Task<T?> ReadOptionalAsync<T>(Func<Task<T>> read, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var result = await read();
                token.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
            {
                token.ThrowIfCancellationRequested();
                Logger.ZLogWarning(
                    $"Clipboard representation could not be read. ErrorType={error.GetType().Name} Code={error.HResult}");
                return default;
            }
        }

        public static async Task<PinnedBitmapInput?> ReadBitmapAsync(IAsyncDataTransfer transfer,
            CancellationToken token)
        {
            foreach (var item in transfer.Items)
            {
                token.ThrowIfCancellationRequested();
                using var bitmap = await item.TryGetBitmapAsync();
                if (bitmap is null) continue;
                if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 128L * 1024 * 1024)
                    throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                using var stream = new MemoryStream();
                await Task.Run(() => bitmap.Save(stream, PngBitmapEncoderOptions.Default), token);
                var raw = item.Formats.Contains(ClipboardWindowShape.Format)
                    ? await ReadOptionalAsync(async () => await item.TryGetRawAsync(ClipboardWindowShape.Format), token)
                    : null;
                var (shape, captureScaling) = ClipboardWindowShape.Read(raw as byte[]);
                return new(stream.ToArray(), shape, captureScaling);
            }

            return null;
        }
    }
}
