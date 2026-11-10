using System.Buffers.Binary;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static class ClipboardWindowShape
    {
        internal static DataFormat<byte[]> Format { get; } =
            ClipboardPlatformFormats.CreateBytesFormat("application/x-ritsukagegif-window-decoration-shape");

        internal static void Write(DataTransferItem item, CaptureWindowShape? shape, double captureScaling)
        {
            var radius = shape.GetValueOrDefault();
            if (!radius.IsValid || !double.IsFinite(captureScaling) || captureScaling is < 1 or > 16 ||
                (radius.IsEmpty && captureScaling is 1)) return;
            var bytes = new byte[24];
            BinaryPrimitives.WriteDoubleLittleEndian(bytes, radius.RadiusX);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(8), radius.RadiusY);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16), captureScaling);
            item.Set(Format, bytes);
        }

        internal static (CaptureWindowShape? Shape, double CaptureScaling) Read(byte[]? bytes)
        {
            if (bytes is not { Length: 24 }) return (null, 1);
            var shape = new CaptureWindowShape(BinaryPrimitives.ReadDoubleLittleEndian(bytes),
                BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(8)));
            var captureScaling = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(16));
            return shape.IsValid && double.IsFinite(captureScaling) && captureScaling is >= 1 and <= 16
                ? (shape.IsEmpty ? null : shape, captureScaling)
                : (null, 1);
        }
    }
}
