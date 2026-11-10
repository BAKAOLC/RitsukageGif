using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private async Task ReplaceWithClipboardAsync(IPinnedWindow source, SemaphoreSlim gate)
        {
            var token = source.OutputLifetime;
            await gate.WaitAsync(token);
            try
            {
                Dispatcher.UIThread.VerifyAccess();
                if (_disposed || !AllWindows.Contains(source) || source.Window.Clipboard is not { } clipboard) return;
                using var transfer = await ReadClipboardCandidateAsync(clipboard.TryGetDataAsync, token);
                if (transfer is null) return;
                var kinds = Preferences.SupportedContentKinds;
                if (kinds == PinnedContentKinds.None) return;
                var snapshot = await PinnedClipboardSnapshot.ReadEnabledAsync(transfer, kinds, token);
                var result = await PinInputSnapshotAsync(snapshot, kinds,
                    () => PinnedClipboardSnapshot.ReadBitmapAsync(transfer, token), false, token);
                if (result.Created is not { } created) return;
                CompleteInputReplacement(source, created);
                PinLogger.ZLogInformation(
                    $"Pinned clipboard replacement completed. Replaced={Preferences.ReplacementAction}");
            }
            finally
            {
                gate.Release();
            }
        }

        private void CompleteInputReplacement(IPinnedWindow source, IPinnedWindow created)
        {
            created.Presentation.SetScreenCenter(source.Presentation.ScreenCenter);
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (Preferences.ReplacementAction)
            {
                case PinnedReplacementAction.KeepSource: break;
                case PinnedReplacementAction.CloseSource: CloseSelection(source, false); break;
                case PinnedReplacementAction.DestroySource: CloseSelection(source, true); break;
            }

            ScheduleSave();
        }
    }
}
