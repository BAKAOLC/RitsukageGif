using System.IO;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Native.Interop;
using Avalonia.Platform;

namespace Avalonia.Native
{
    internal class AvaloniaNativeCursor : ICursorImpl
    {
        // ReSharper disable once ConvertToPrimaryConstructor
        public AvaloniaNativeCursor(IAvnCursor cursor)
        {
            Cursor = cursor;
        }

        public IAvnCursor? Cursor { get; private set; }

        public void Dispose()
        {
            Cursor?.Dispose();
            Cursor = null;
        }
    }

    internal class CursorFactory : ICursorFactory
    {
        private readonly IAvnCursorFactory _native;

        // ReSharper disable once ConvertToPrimaryConstructor
        public CursorFactory(IAvnCursorFactory native)
        {
            _native = native;
        }

        public ICursorImpl GetCursor(StandardCursorType cursorType)
        {
            var cursor = _native.GetCursor((AvnStandardCursorType)cursorType);
            return new AvaloniaNativeCursor(cursor);
        }

        public unsafe ICursorImpl CreateCursor(Bitmap cursor, PixelPoint hotSpot)
        {
            {
                using var ms = new MemoryStream();
                cursor.Save(ms, PngBitmapEncoderOptions.Default);

                var imageData = ms.ToArray();

                fixed (void* ptr = imageData)
                {
                    var avnCursor = _native.CreateCustomCursor(ptr, new(imageData.Length),
                        new() { Width = hotSpot.X, Height = hotSpot.Y });

                    return new AvaloniaNativeCursor(avnCursor);
                }
            }
        }
    }
}
