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

        public string? FormulaSource { get; private init; }
        public string? HtmlSourceUrl { get; private init; }
        public PinnedHtmlSelection? HtmlSelection { get; private init; }

        public static async Task<PinnedClipboardSnapshot> ReadEnabledAsync(IAsyncDataTransfer transfer,
            PinnedContentKinds kinds, CancellationToken token)
        {
            var images = new List<(string, byte[])>();
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
                        }
                    }
                }

            var bitmapBytes = images.Count == 0 && (kinds & PinnedContentKinds.Images) != 0
                ? await ReadOptionalAsync(() => ReadBitmapAsync(transfer, token), token)
                : null;

            var files = (kinds & (PinnedContentKinds.Files | PinnedContentKinds.Images)) != 0
                ? await ReadOptionalAsync(async () => await transfer.TryGetFilesAsync(), token)
                : null;
            var paths = files?.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            var text = (kinds & (PinnedContentKinds.Text | PinnedContentKinds.Color)) != 0
                ? await ReadOptionalAsync(async () => await transfer.TryGetTextAsync(), token)
                : null;
            string? html = null;
            string? htmlSourceUrl = null;
            PinnedHtmlSelection? htmlSelection = null;
            if ((kinds & (PinnedContentKinds.Text | PinnedContentKinds.Color)) != 0)
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
            return new(images, bitmapBytes, paths, text, html)
            {
                FormulaSource = formula, HtmlSourceUrl = htmlSourceUrl, HtmlSelection = htmlSelection,
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

        public static async Task<byte[]?> ReadBitmapAsync(IAsyncDataTransfer transfer, CancellationToken token)
        {
            using var bitmap = await transfer.TryGetBitmapAsync();
            if (bitmap is null) return null;
            if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 128L * 1024 * 1024)
                throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            using var stream = new MemoryStream();
            await Task.Run(() => bitmap.Save(stream, PngBitmapEncoderOptions.Default), token);
            return stream.ToArray();
        }
    }
}
