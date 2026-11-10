using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedDropInputSnapshot : IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Pins.DropSnapshot");

        private readonly PinnedDropDataTransfer _transfer;
        private readonly string[] _files;
        private readonly byte[]? _bitmap;
        private readonly Bitmap? _ownedBitmap;
        private readonly PinnedContentKinds _kinds;
        private readonly (CaptureWindowShape? Shape, double CaptureScaling) _bitmapShape;
        private readonly IReadOnlyDictionary<string, (CaptureWindowShape? Shape, double CaptureScaling)>? _fileShapes;

        private PinnedDropInputSnapshot(PinnedDropDataTransfer transfer, string[] files, byte[]? bitmap,
            Bitmap? ownedBitmap,
            PinnedContentKinds kinds,
            IReadOnlyDictionary<string, (CaptureWindowShape? Shape, double CaptureScaling)>? fileShapes,
            (CaptureWindowShape? Shape, double CaptureScaling) bitmapShape)
        {
            _transfer = transfer;
            _files = files;
            _bitmap = bitmap;
            _ownedBitmap = ownedBitmap;
            _kinds = kinds;
            _fileShapes = fileShapes;
            _bitmapShape = bitmapShape;
        }

        public void Dispose()
        {
            _ownedBitmap?.Dispose();
        }

        // Call before returning from Drop or reaching its first await. No source object survives this method.
        internal static PinnedDropInputSnapshot Capture(IDataTransfer transfer, PinnedContentKinds kinds,
            bool disposeSourceBitmap, CancellationToken token)
        {
            var formats = GetFormats(kinds, true);
            var items = new List<IAsyncDataTransferItem>();
            var files = new List<string>();
            Dictionary<string, (CaptureWindowShape? Shape, double CaptureScaling)>? fileShapes = null;
            IDataTransferItem? bitmapItem = null;
            (CaptureWindowShape? Shape, double CaptureScaling) bitmapShape = (null, 1);
            foreach (var item in transfer.Items)
            {
                token.ThrowIfCancellationRequested();
                var values = new Dictionary<DataFormat, object>();
                foreach (var format in formats)
                {
                    var advertised = item.Formats.FirstOrDefault(candidate => candidate == format);
                    if (advertised is null) continue;
                    var raw = ReadOptional(() => item.TryGetRaw(advertised));
                    switch (raw)
                    {
                        case byte[] bytes:
                        {
                            if (format == ClipboardWindowShape.Format && bytes.Length != 24) continue;
                            var isImage = (kinds & PinnedContentKinds.Images) != 0 &&
                                          ClipboardPlatformFormats.ImageNames.Any(name =>
                                              DataFormat.CreateBytesPlatformFormat(name) == format);
                            if (isImage && bytes.Length is 0 or > PinnedImageContent.MaximumEncodedBytes)
                                continue;
                            values.Add(advertised, bytes.ToArray());
                            break;
                        }
                        case string text: values.Add(advertised, text); break;
                    }
                }

                if (values.Count > 0) items.Add(new PinnedDropDataTransferItem(values));
                if ((kinds & (PinnedContentKinds.Files | PinnedContentKinds.Images)) != 0 &&
                    item.Formats.Contains(DataFormat.File) &&
                    ReadOptional(() => item.TryGetFile()?.TryGetLocalPath()) is { } path)
                {
                    files.Add(path);
                    if (values.TryGetValue(ClipboardWindowShape.Format, out var shapeBytes))
                    {
                        var shape = ClipboardWindowShape.Read(shapeBytes as byte[]);
                        if (shape.Shape is not null || shape.CaptureScaling is not 1)
                            (fileShapes ??= new(OperatingSystem.IsWindows()
                                ? StringComparer.OrdinalIgnoreCase
                                : StringComparer.Ordinal))[path] = shape;
                    }
                }

                if ((kinds & PinnedContentKinds.Images) != 0 && bitmapItem is null &&
                    item.Formats.Contains(DataFormat.Bitmap))
                {
                    bitmapItem = item;
                    bitmapShape = ClipboardWindowShape.Read(
                        values.GetValueOrDefault(ClipboardWindowShape.Format) as byte[]);
                }
            }

            token.ThrowIfCancellationRequested();
            Bitmap? ownedBitmap = null;
            byte[]? bitmapBytes = null;
            if (bitmapItem is not null && ReadOptional(bitmapItem.TryGetBitmap) is { } bitmap)
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    ownedBitmap = ReadOptional(() => CloneBitmap(bitmap));
                    // ReSharper disable once AccessToDisposedClosure
                    if (ownedBitmap is null) bitmapBytes = ReadOptional(() => EncodeBitmap(bitmap));
                }
                finally
                {
                    if (disposeSourceBitmap) bitmap.Dispose();
                }

            return new(new(items), [.. files], bitmapBytes, ownedBitmap, kinds, fileShapes, bitmapShape);
        }

        internal static bool CanRead(IDataTransfer transfer, PinnedContentKinds kinds)
        {
            var formats = transfer.Formats;
            return GetFormats(kinds).Any(formats.Contains) ||
                   ((kinds & PinnedContentKinds.Images) != 0 && formats.Contains(DataFormat.Bitmap)) ||
                   ((kinds & (PinnedContentKinds.Files | PinnedContentKinds.Images)) != 0 &&
                    formats.Contains(DataFormat.File));
        }

        private static List<DataFormat> GetFormats(PinnedContentKinds kinds, bool includeSourceMetadata = false)
        {
            var formats = new List<DataFormat>();
            if ((kinds & PinnedContentKinds.Images) != 0)
                formats.AddRange(ClipboardPlatformFormats.ImageNames.Select(DataFormat.CreateBytesPlatformFormat));
            if (includeSourceMetadata && (kinds & PinnedContentKinds.Images) != 0)
                formats.Add(ClipboardWindowShape.Format);
            if ((includeSourceMetadata && kinds != PinnedContentKinds.None) ||
                (kinds & (PinnedContentKinds.Text | PinnedContentKinds.Color)) != 0)
            {
                formats.Add(DataFormat.Text);
                formats.AddRange(ClipboardPlatformFormats.HtmlNames.Select(DataFormat.CreateBytesPlatformFormat));
            }

            if ((kinds & PinnedContentKinds.Formula) != 0)
            {
                formats.Add(ClipboardPlatformFormats.CreateBytesFormat("application/x-latex"));
                formats.Add(ClipboardPlatformFormats.CreateBytesFormat("text/x-tex"));
            }

            return formats;
        }

        internal async Task<PinnedClipboardSnapshot> ReadEnabledAsync(CancellationToken token)
        {
            var snapshot = await PinnedClipboardSnapshot.ReadEnabledAsync(_transfer, _kinds, token);
            var bitmap = snapshot.Images.Count == 0 && _files.Length == 0 ? await ReadBitmapAsync(token) : null;
            token.ThrowIfCancellationRequested();
            return snapshot with
            {
                Files = _files, Bitmap = bitmap?.Png, FileShapes = _fileShapes,
                BitmapWindowShape = bitmap?.WindowShape ?? default, BitmapCaptureScaling = bitmap?.CaptureScaling ?? 1,
            };
        }

        internal async Task<PinnedBitmapInput?> ReadBitmapAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var bitmap = _ownedBitmap is null
                ? _bitmap
                : await Task.Run(() => ReadOptional(() => EncodeBitmap(_ownedBitmap)), token);
            token.ThrowIfCancellationRequested();
            return bitmap is null ? null : new(bitmap, _bitmapShape.Shape, _bitmapShape.CaptureScaling);
        }

        private static WriteableBitmap? CloneBitmap(Bitmap bitmap)
        {
            CheckBitmapSize(bitmap);
            if (bitmap.Format is not { } format || bitmap.AlphaFormat is not { } alphaFormat) return null;
            var clone = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, format, alphaFormat);
            if (clone.Format != format || clone.AlphaFormat != alphaFormat)
            {
                clone.Dispose();
                return null;
            }

            try
            {
                using var framebuffer = clone.Lock();
                bitmap.CopyPixels(new(bitmap.PixelSize), framebuffer.Address,
                    framebuffer.RowBytes * framebuffer.Size.Height, framebuffer.RowBytes);
                return clone;
            }
            catch
            {
                clone.Dispose();
                throw;
            }
        }

        private static byte[] EncodeBitmap(Bitmap bitmap)
        {
            CheckBitmapSize(bitmap);
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }

        private static void CheckBitmapSize(Bitmap bitmap)
        {
            if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 128L * 1024 * 1024)
                throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
        }

        private static T? ReadOptional<T>(Func<T?> read)
        {
            try
            {
                return read();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
            {
                Logger.ZLogWarning(
                    $"Drop representation could not be read. ErrorType={error.GetType().Name} Code={error.HResult}");
                return default;
            }
        }
    }
}
