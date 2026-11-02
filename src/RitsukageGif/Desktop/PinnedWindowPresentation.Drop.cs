using Avalonia.Input;
using Avalonia.Interactivity;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        private static readonly ILogger DropLogger = LogHub.GetLogger("Desktop.Pins.Drop");

        internal void ConnectDrop(IPinnedWindow target, Func<bool> canDrop,
            Func<PinnedContentKinds> supportedKinds,
            Func<PinnedDropInputSnapshot, PinnedContentKinds, CancellationToken, Task> consume)
        {
            DragDrop.SetAllowDrop(_owner, true);
            _owner.AddHandler(DragDrop.DragEnterEvent, OnDragOver, RoutingStrategies.Bubble);
            _owner.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
            _owner.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);
            return;

            void OnDragOver(object? sender, DragEventArgs args)
            {
                args.Handled = true;
                try
                {
                    var origin = PinnedDragRegistration.Find(args.DataTransfer);
                    args.DragEffects = canDrop() && !ReferenceEquals(origin?.Source, target) &&
                                       PinnedDropInputSnapshot.CanRead(args.DataTransfer, supportedKinds())
                        ? args.DragEffects & DragDropEffects.Copy
                        : DragDropEffects.None;
                }
                catch (Exception error)
                {
                    args.DragEffects = DragDropEffects.None;
                    DropLogger.ZLogWarning($"Pinned drag formats could not be read. ErrorType={error.GetType().Name}");
                }
            }

            // ReSharper disable once AsyncVoidEventHandlerMethod
            async void OnDrop(object? sender, DragEventArgs args)
            {
                args.Handled = true;
                args.DragEffects = DragDropEffects.None;
                var token = CancellationToken.None;
                try
                {
                    var origin = PinnedDragRegistration.Find(args.DataTransfer);
                    if (!canDrop() || ReferenceEquals(origin?.Source, target)) return;
                    token = target.OutputLifetime;
                    var kinds = supportedKinds();
                    if (!PinnedDropInputSnapshot.CanRead(args.DataTransfer, kinds)) return;
                    // Snapshot the native event data before the first await.
                    using var snapshot = PinnedDropInputSnapshot.Capture(args.DataTransfer, kinds,
                        origin?.DisposeSourceBitmap ?? true, token);
                    args.DragEffects = DragDropEffects.Copy;
                    using var input = PinnedInputContext.Use(PinnedInputContext.Current & PinnedInputOrigin.Keyboard);
                    await consume(snapshot, kinds, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    DropLogger.ZLogDebug($"Pinned input drop cancelled.");
                }
                catch (Exception error)
                {
                    DropLogger.ZLogError(error, $"Pinned input drop failed.");
                }
            }
        }
    }
}
