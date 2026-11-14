using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private void AttachDrop(IPinnedWindow source)
        {
            var gate = new SemaphoreSlim(1);
            source.Presentation.PasteRequested = () => ReplaceWithClipboardAsync(source, gate);
            source.Presentation.ConnectDrop(source, CanDrop, () => Preferences.SupportedContentKinds, ConsumeAsync);
            return;

            bool CanDrop()
            {
                return !_disposed && AllWindows.Contains(source) && !source.IsIncomingDropLocked;
            }

            async Task ConsumeAsync(PinnedDropInputSnapshot input, PinnedContentKinds kinds, CancellationToken token)
            {
                await gate.WaitAsync(token);
                try
                {
                    Dispatcher.UIThread.VerifyAccess();
                    if (!CanDrop()) return;
                    var snapshot = await input.ReadEnabledAsync(token);
                    var prepared = await PinInputSnapshotAsync(snapshot, kinds, () => input.ReadBitmapAsync(token),
                        true, token);
                    var result = await ConfirmClipboardPinAsync(prepared, source.Window, token);
                    if (result.Created is not { } created) return;
                    CompleteInputReplacement(source, created);
                    PinLogger.ZLogInformation($"Pinned input drop completed. Replaced={Preferences.ReplacementAction}");
                }
                finally
                {
                    gate.Release();
                }
            }
        }
    }
}
