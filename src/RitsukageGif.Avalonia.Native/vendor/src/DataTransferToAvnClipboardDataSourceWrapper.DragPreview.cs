using System;
using Avalonia.Native.Interop;
using Avalonia.Platform;
using RitsukageGif.DragPreview;

namespace Avalonia.Native
{
    internal sealed partial class DataTransferToAvnClipboardDataSourceWrapper
    {
        AvnPixelSize IAvnDragPreview.PixelSize
        {
            get
            {
                var size = (_dataTransfer as IDragPreview)?.Preview.PixelSize ?? default;
                return new() { Width = size.Width, Height = size.Height };
            }
        }

        AvnPoint IAvnDragPreview.HotSpot
        {
            get
            {
                var point = (_dataTransfer as IDragPreview)?.HotSpot ?? default;
                return new() { X = point.X, Y = point.Y };
            }
        }

        double IAvnDragPreview.Scaling => (_dataTransfer as IDragPreview)?.PixelScale ?? 1;

        unsafe void IAvnDragPreview.CopyPixels(void* buffer, int bufferSize)
        {
            if (_dataTransfer is not IDragPreview preview)
                throw new InvalidOperationException("The drag source has no preview.");
            var size = preview.Preview.PixelSize;
            var stride = checked(size.Width * 4);
            if (buffer == null || bufferSize != checked(stride * size.Height))
                throw new ArgumentException("The drag preview destination size is invalid.");
            using var target = new LockedFramebuffer((IntPtr)buffer, size, stride, new(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul, null);
            preview.Preview.CopyPixels(target);
        }
    }
}
