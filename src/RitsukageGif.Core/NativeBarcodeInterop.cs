using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    internal static partial class NativeBarcodeInterop
    {
        private const string Library = "RitsukageBarcode";

        [LibraryImport(Library, EntryPoint = "ZXing_ImageView_new_checked")]
        internal static unsafe partial nint CreateImage(byte* data, int size, int width, int height, int format,
            int rowStride, int pixelStride);

        [LibraryImport(Library, EntryPoint = "ZXing_ImageView_delete")]
        internal static partial void DeleteImage(nint image);

        [LibraryImport(Library, EntryPoint = "ZXing_ReaderOptions_new")]
        internal static partial nint CreateOptions();

        [LibraryImport(Library, EntryPoint = "ZXing_ReaderOptions_delete")]
        internal static partial void DeleteOptions(nint options);

        [LibraryImport(Library, EntryPoint = "ZXing_ReaderOptions_setFormats")]
        internal static unsafe partial void SetFormats(NativeBarcodeOptionsHandle options, uint* formats, int count);

        [LibraryImport(Library, EntryPoint = "ZXing_ReaderOptions_setMaxNumberOfSymbols")]
        internal static partial void SetMaximumSymbols(NativeBarcodeOptionsHandle options, int count);

        [LibraryImport(Library, EntryPoint = "ZXing_ReadBarcodes")]
        internal static partial nint Read(NativeBarcodeImageHandle image, NativeBarcodeOptionsHandle options);

        [LibraryImport(Library, EntryPoint = "ZXing_Barcodes_delete")]
        internal static partial void DeleteResults(nint results);

        [LibraryImport(Library, EntryPoint = "ZXing_Barcodes_size")]
        internal static partial int GetCount(NativeBarcodeResultsHandle results);

        [LibraryImport(Library, EntryPoint = "ZXing_Barcodes_at")]
        internal static partial nint GetAt(NativeBarcodeResultsHandle results, int index);

        [LibraryImport(Library, EntryPoint = "ZXing_Barcode_isValid")]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static partial bool IsValid(nint barcode);

        [LibraryImport(Library, EntryPoint = "ZXing_Barcode_text")]
        internal static partial nint GetText(nint barcode);

        [LibraryImport(Library, EntryPoint = "ZXing_Barcode_format")]
        internal static partial uint GetFormat(nint barcode);

        [LibraryImport(Library, EntryPoint = "ZXing_Barcode_position")]
        internal static partial NativeBarcodePosition GetPosition(nint barcode);

        [LibraryImport(Library, EntryPoint = "ZXing_BarcodeFormatToString")]
        internal static partial nint GetFormatName(uint format);

        [LibraryImport(Library, EntryPoint = "ZXing_LastErrorMsg")]
        private static partial nint GetLastError();

        [LibraryImport(Library, EntryPoint = "ZXing_free")]
        private static partial void Free(nint memory);

        internal static nint Require(nint pointer)
        {
            if (pointer != 0) return pointer;
            throw new InvalidOperationException(ReadString(GetLastError()));
        }

        internal static string ReadString(nint pointer)
        {
            if (pointer == 0) return string.Empty;
            try
            {
                return Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
            }
            finally
            {
                Free(pointer);
            }
        }
    }
}
