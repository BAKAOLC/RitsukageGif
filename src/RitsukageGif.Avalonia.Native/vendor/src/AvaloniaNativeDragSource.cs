using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    internal class AvaloniaNativeDragSource : IPlatformDragSource
    {
        // ReSharper disable once NotAccessedField.Local
        private readonly IAvaloniaNativeFactory _factory;

        // ReSharper disable once ConvertToPrimaryConstructor
        public AvaloniaNativeDragSource(IAvaloniaNativeFactory factory)
        {
            _factory = factory;
        }

        public Task<DragDropEffects> DoDragDropAsync(
            PointerPressedEventArgs triggerEvent,
            IDataTransfer dataTransfer,
            DragDropEffects allowedEffects)
        {
            // Sanity check
            var tl = TopLevel.GetTopLevel(triggerEvent.Source as Visual);
            if (tl?.PlatformImpl is not TopLevelImpl view)
                throw new ArgumentException("The triggering control must belong to an Avalonia native top level.",
                    nameof(triggerEvent));

            triggerEvent.Pointer.Capture(null);

            var tcs = new TaskCompletionSource<DragDropEffects>();

            using (var cb = new DndCallback(tcs))
            {
                var dataSource = new DataTransferToAvnClipboardDataSourceWrapper(dataTransfer);

                view.BeginDraggingSession((AvnDragDropEffects)allowedEffects,
                    triggerEvent.GetPosition(tl).ToAvnPoint(), dataSource, cb,
                    GCHandle.ToIntPtr(GCHandle.Alloc(dataTransfer)));
            }

            return tcs.Task;
        }

        private class DndCallback : NativeCallbackBase, IAvnDndResultCallback
        {
            private TaskCompletionSource<DragDropEffects>? _tcs;

            // ReSharper disable once ConvertToPrimaryConstructor
            public DndCallback(TaskCompletionSource<DragDropEffects> tcs)
            {
                _tcs = tcs;
            }

            public void OnDragAndDropComplete(AvnDragDropEffects effect)
            {
                _tcs?.TrySetResult((DragDropEffects)effect);
                _tcs = null;
            }
        }
    }
}
