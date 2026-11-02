using System.Collections.ObjectModel;

namespace RitsukageGif.Core
{
    public sealed partial class BarcodeRecognitionService
    {
        private static unsafe ReadOnlyCollection<BarcodeRecognitionResult> Recognize(byte[] luminance, int width,
            int height, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (luminance.AsSpan().IndexOfAnyExcept(luminance[0]) < 0)
                return ReadOnlyCollection<BarcodeRecognitionResult>.Empty;
            using var options = new NativeBarcodeOptionsHandle();
            var formats = stackalloc uint[] { 0x6c2a, 0x207a, 0x2064, 0x204c, 0x2051, 0x6d51, 0x7251 };
            NativeBarcodeInterop.SetFormats(options, formats, 7);
            NativeBarcodeInterop.SetMaximumSymbols(options, 0);
            fixed (byte* data = luminance)
            {
                using var image = new NativeBarcodeImageHandle(NativeBarcodeInterop.CreateImage(data,
                    luminance.Length, width, height, 0x01000000, width, 1));
                var output = new List<BarcodeRecognitionResult>();
                ReadResults(output, image, options, token);
                token.ThrowIfCancellationRequested();
                NativeBarcodeInterop.SetFormats(options, formats, 1);
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = luminance.AsSpan(y * width, width);
                    foreach (ref var pixel in row) pixel = (byte)~pixel;
                }

                ReadResults(output, image, options, token);
                return output.AsReadOnly();
            }
        }

        private static void ReadResults(List<BarcodeRecognitionResult> output, NativeBarcodeImageHandle image,
            NativeBarcodeOptionsHandle options, CancellationToken token)
        {
            using var results = new NativeBarcodeResultsHandle(NativeBarcodeInterop.Read(image, options));
            token.ThrowIfCancellationRequested();
            var count = NativeBarcodeInterop.GetCount(results);
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                var barcode = NativeBarcodeInterop.GetAt(results, index);
                if (!NativeBarcodeInterop.IsValid(barcode)) continue;
                var position = NativeBarcodeInterop.GetPosition(barcode);
                var corners = Array.AsReadOnly<BarcodeRecognitionPoint>(
                [
                    new(position.TopLeft.X, position.TopLeft.Y),
                    new(position.TopRight.X, position.TopRight.Y),
                    new(position.BottomRight.X, position.BottomRight.Y),
                    new(position.BottomLeft.X, position.BottomLeft.Y),
                ]);
                output.Add(new(NativeBarcodeInterop.ReadString(NativeBarcodeInterop.GetText(barcode)),
                    FormatName(NativeBarcodeInterop.GetFormat(barcode)), corners) { Outline = corners });
            }
        }

        private static string FormatName(uint format)
        {
            return format switch
            {
                0x207a or 0x637a or 0x727a => "AZTEC",
                0x2064 => "DATA_MATRIX",
                0x204c or 0x634c => "PDF_417",
                0x2051 or 0x3151 or 0x3251 => "QR_CODE",
                0x6d51 => "MICRO_QR_CODE",
                0x7251 => "RMQR_CODE",
                0x2046 => "CODABAR",
                0x2041 or 0x7341 or 0x6541 => "CODE_39",
                0x2047 => "CODE_93",
                0x2043 => "CODE_128",
                0x2049 or 0x3449 => "ITF",
                0x2065 or 0x6f65 or 0x7365 or 0x4f65 => "RSS_14",
                0x6565 or 0x4565 => "RSS_EXPANDED",
                0x6c65 => "DATABAR_LIMITED",
                0x3145 or 0x6945 => "EAN_13",
                0x3845 => "EAN_8",
                0x6145 => "UPC_A",
                0x6545 => "UPC_E",
                _ => NativeBarcodeInterop.ReadString(NativeBarcodeInterop.GetFormatName(format)),
            };
        }
    }
}
