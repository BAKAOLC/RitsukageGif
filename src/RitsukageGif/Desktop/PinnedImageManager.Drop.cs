using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private void AttachDrop(IPinnedWindow source)
        {
            var gate = new SemaphoreSlim(1);
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
                    var result = await PinInputSnapshotAsync(snapshot, kinds, () => input.ReadBitmapAsync(token), true,
                        token);
                    if (result.Created is not { } created) return;
                    created.Presentation.SetScreenCenter(source.Presentation.ScreenCenter);
                    // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                    switch (Preferences.ReplacementAction)
                    {
                        case PinnedReplacementAction.KeepSource: break;
                        case PinnedReplacementAction.CloseSource: CloseSelection(source, false); break;
                        case PinnedReplacementAction.DestroySource: CloseSelection(source, true); break;
                    }

                    ScheduleSave();
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
