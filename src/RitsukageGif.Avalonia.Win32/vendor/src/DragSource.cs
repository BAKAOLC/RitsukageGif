using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.Win32.Interop;
using Avalonia.Win32.Win32Com;
using MicroCom.Runtime;
using RitsukageGif.DragPreview;

namespace Avalonia.Win32
{
    internal sealed class DragSource : IPlatformDragSource
    {
        public Task<DragDropEffects> DoDragDropAsync(
            PointerPressedEventArgs triggerEvent,
            IDataTransfer dataTransfer,
            DragDropEffects allowedEffects)
        {
            Dispatcher.UIThread.VerifyAccess();

            triggerEvent.Pointer.Capture(null);

            using var dataObject = new DataTransferToOleDataObjectWrapper(dataTransfer);
            var allowed = OleDropTarget.ConvertDropEffect(allowedEffects);
            using var preview = dataTransfer is IDragPreview dragPreview
                ? Win32DragPreview.TryCreate(dragPreview, allowed)
                : null;
            using var src = new OleDragSource(preview);

            var objPtr = dataObject.GetNativeIntPtr<IDataObject>();
            var srcPtr = src.GetNativeIntPtr<IDropSource>();

            UnmanagedMethods.DoDragDrop(objPtr, srcPtr, (int)allowed, out var finalEffect);

            // Force releasing of internal wrapper to avoid memory leak, if drop target keeps com reference.
            dataObject.ReleaseDataTransfer();

            return Task.FromResult(OleDropTarget.ConvertDropEffect((DropEffect)finalEffect));
        }
    }
}
