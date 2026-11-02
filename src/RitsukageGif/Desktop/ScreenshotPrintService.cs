using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotPrintService
    {
        private static readonly ConcurrentDictionary<nint, CancellationToken> Jobs = new();
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Print");

        internal static bool IsSupported =>
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

        internal static bool IsAvailable => NativePrintCapabilities.Current.Available;

        internal static Task<PrintCapabilities> GetCapabilitiesAsync(CancellationToken token)
        {
            return NativePrintCapabilities.GetAsync(token);
        }

        internal static async Task<bool> PrintAsync(nint owner, byte[] image, string title, CancellationToken token)
        {
            var capability = await GetCapabilitiesAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!capability.Available) throw new PlatformNotSupportedException(UiText.Get("PrintUnavailable"));
            return await ScreenshotPrintProcess.PrintAsync(owner, image, title, token).ConfigureAwait(false);
        }

        internal static bool PrintFileNative(nint owner, string input, string title, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var codec = SKCodec.Create(input) ??
                              throw new InvalidDataException("The image cannot be decoded for printing.");
            return PrintCore(owner, [new(input, 96, codec.Info.Width * 72d / 96, codec.Info.Height * 72d / 96)], title,
                false, null, token);
        }

        private static unsafe bool PrintCore(nint owner, IReadOnlyList<PrintPageFile> pages, string title,
            bool requirePng, Func<PrinterPageMetrics, CancellationToken, IReadOnlyList<PrintPageFile>>? prepare,
            CancellationToken token)
        {
            if (prepare is null) ValidatePages(pages, requirePng, token);

            var initialized = CoInitializeEx(0, 2);
            if (initialized < 0) Marshal.ThrowExceptionForHR(initialized);
            PrintPageRange* ranges = null;
            var dialog = new PrintDialog
            {
                Size = (uint)Marshal.SizeOf<PrintDialog>(), Owner = owner,
                Flags = 0x100 | 4 | 0x800000 | 0x40000 | 0x100000 | (prepare is not null || pages.Count == 1 ? 8u : 0u),
                StartPage = uint.MaxValue, Copies = 1, MinimumPage = 1,
                MaximumPage = prepare is null ? (uint)pages.Count : 4096,
                PageRanges = 1, MaximumPageRanges = (uint)Math.Max(1, pages.Count),
            };
            try
            {
                ranges = (PrintPageRange*)NativeMemory.AllocZeroed((nuint)Math.Max(1, pages.Count),
                    (nuint)sizeof(PrintPageRange));
                ranges[0] = new() { From = 1, To = prepare is null ? (uint)pages.Count : 4096 };
                dialog.Ranges = (nint)ranges;
                var threadId = GetCurrentThreadId();
                using var cancelPendingDialog = new Timer(_ => CloseDialogs(threadId), null, Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
                // ReSharper disable once AccessToDisposedClosure
                using var cancel = token.Register(() =>
                    cancelPendingDialog.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(250)));
                token.ThrowIfCancellationRequested();
                Logger.ZLogDebug($"Native print dialog opened. DocumentPages={pages.Count}");
                var result = PrintDlgExW(ref dialog);
                token.ThrowIfCancellationRequested();
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                if (dialog.ResultAction != 1 || dialog.Context == 0)
                {
                    Logger.ZLogDebug($"Native print dialog canceled.");
                    return false;
                }

                PrinterPageMetrics? metrics = null;
                if (prepare is not null)
                {
                    metrics = new(GetDeviceCaps(dialog.Context, 110), GetDeviceCaps(dialog.Context, 111),
                        GetDeviceCaps(dialog.Context, 8), GetDeviceCaps(dialog.Context, 10),
                        GetDeviceCaps(dialog.Context, 112), GetDeviceCaps(dialog.Context, 113),
                        GetDeviceCaps(dialog.Context, 88), GetDeviceCaps(dialog.Context, 90));
                    metrics.Validate();
                    pages = prepare(metrics, token);
                    ValidatePages(pages, true, token);
                }

                Jobs[dialog.Context] = token;
                if (SetAbortProc(dialog.Context, &AbortRequested) <= 0) throw new Win32Exception();
                if (dialog.PageRanges > dialog.MaximumPageRanges)
                    throw new InvalidDataException("The printer returned invalid page ranges.");
                var selected = DocumentPrintLayout.SelectPages(pages.Count,
                    prepare is not null || (dialog.Flags & 2) == 0,
                    new(ranges, (int)dialog.PageRanges));
                var width = GetDeviceCaps(dialog.Context, 8);
                var height = GetDeviceCaps(dialog.Context, 10);
                var dpiX = GetDeviceCaps(dialog.Context, 88);
                var dpiY = GetDeviceCaps(dialog.Context, 90);
                var name = Marshal.StringToCoTaskMemUni(title);
                try
                {
                    var info = new DocumentInfo { Size = Marshal.SizeOf<DocumentInfo>(), Name = name };
                    if (StartDocW(dialog.Context, ref info) <= 0) throw new Win32Exception();
                    try
                    {
                        for (var index = 0; index < pages.Count; index++)
                        {
                            if (!selected[index]) continue;
                            token.ThrowIfCancellationRequested();
                            var page = pages[index];
                            using var source = SKBitmap.Decode(page.Path) ??
                                               throw new InvalidDataException("A print page cannot be decoded.");
                            using var bitmap = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888,
                                SKAlphaType.Opaque);
                            using (var canvas = new SKCanvas(bitmap))
                            {
                                canvas.Clear(SKColors.White);
                                canvas.DrawBitmap(source, 0, 0);
                            }

                            var placement = metrics?.Placement(page.WidthPoints, page.HeightPoints) ??
                                            DocumentPrintLayout.Place(page.WidthPoints, page.HeightPoints, width,
                                                height, dpiX, dpiY);
                            if (StartPage(dialog.Context) <= 0) throw new Win32Exception();
                            var header = new BitmapHeader
                            {
                                Size = 40, Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, BitCount = 32,
                                ImageSize = (uint)bitmap.ByteCount,
                            };
                            _ = SetStretchBltMode(dialog.Context, 4);
                            _ = SetBrushOrgEx(dialog.Context, 0, 0, 0);
                            var rows = StretchDIBits(dialog.Context, placement.X, placement.Y, placement.Width,
                                placement.Height,
                                0, 0, bitmap.Width, bitmap.Height, bitmap.GetPixels(), ref header, 0, 0xCC0020);
                            if (rows is 0 or -1) throw new Win32Exception();
                            token.ThrowIfCancellationRequested();
                            if (EndPage(dialog.Context) <= 0) throw new Win32Exception();
                            Logger.ZLogDebug($"A document page was spooled. PageNumber={index + 1}");
                        }

                        token.ThrowIfCancellationRequested();
                        if (EndDoc(dialog.Context) <= 0) throw new Win32Exception();
                    }
                    catch
                    {
                        _ = AbortDoc(dialog.Context);
                        throw;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(name);
                }

                Logger.ZLogInformation($"Print job completed. DocumentPages={pages.Count}");
                return true;
            }
            finally
            {
                if (dialog.Context != 0)
                {
                    Jobs.TryRemove(dialog.Context, out _);
                    _ = DeleteDC(dialog.Context);
                }

                if (dialog.DeviceMode != 0) _ = GlobalFree(dialog.DeviceMode);
                if (dialog.DeviceNames != 0) _ = GlobalFree(dialog.DeviceNames);
                NativeMemory.Free(ranges);
                if (initialized >= 0) CoUninitialize();
            }
        }

        internal static bool PrintSelectedNative(nint owner, string title,
            Func<PrinterPageMetrics, CancellationToken, IReadOnlyList<PrintPageFile>> prepare,
            CancellationToken token)
        {
            return PrintCore(owner, [], title, true, prepare, token);
        }

        private static void ValidatePages(IReadOnlyList<PrintPageFile> pages, bool requirePng,
            CancellationToken token)
        {
            if (pages.Count is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(pages));
            foreach (var page in pages)
            {
                token.ThrowIfCancellationRequested();
                if (requirePng) DocumentPrintLayout.Validate(page.Dpi, page.WidthPoints, page.HeightPoints);
                using var codec = SKCodec.Create(page.Path) ??
                                  throw new InvalidDataException("A print page cannot be decoded.");
                if (requirePng && codec.EncodedFormat != SKEncodedImageFormat.Png)
                    throw new InvalidDataException("A document print page is not PNG.");
                if (codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                    codec.Info.Width * (double)codec.Info.Height * 4 > 256L * 1024 * 1024)
                    throw new InvalidDataException("A print page exceeds the supported pixel budget.");
                DocumentPrintLayout.ValidateRasterSize(codec.Info.Width, codec.Info.Height, page.Dpi,
                    page.WidthPoints, page.HeightPoints);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int AbortRequested(nint context, int error)
        {
            return Jobs.TryGetValue(context, out var token) && token.IsCancellationRequested ? 0 : 1;
        }

        private static unsafe void CloseDialogs(uint thread)
        {
            _ = EnumThreadWindows(thread, &CloseDialog, 0);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int CloseDialog(nint window, nint parameter)
        {
            _ = PostMessageW(window, 0x10, 0, 0);
            return 1;
        }
    }
}
