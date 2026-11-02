using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task<bool> ExportScreenshotDocumentAsync(Window? owner, ScreenshotResult result,
            CancellationToken token)
        {
            var profile = Settings.ScreenshotSave.ManualProfile();
            var name = Path.ChangeExtension(profile.CreateFileName(ScreenshotName(result)), ".pdf");
            var path = await DocumentExportWindow.ShowAsync(owner, result.Png, name, token,
                profile.ResolveDirectory(Settings.OutputDirectory));
            if (path is null) return false;
            LastScreenshotOutput = new(OutputColorMode.Sdr, [path]);
            SetStatus(UiText.Format("AnimationExported", path));
            try
            {
                await ScreenshotSaveCompletion.RunAsync([path], profile, _services.Context.Clipboard,
                    _services.Context.StorageProvider,
                    () => CopyScreenshotAsync(result with { ColorMode = OutputColorMode.Sdr, Hdr = null },
                        CancellationToken.None),
                    ShowOutputNotification, CancellationToken.None);
            }
            catch (Exception error)
            {
                SetStatus(error.Message);
            }
            finally
            {
                QueueCleanup([path], profile.AfterSave == ScreenshotAfterSaveAction.CopyFile ? [path] : null);
            }

            return true;
        }

        private Task<bool> PrintScreenshotDocumentAsync(Window? owner, ScreenshotResult result,
            CancellationToken token)
        {
            return ScreenshotPrintService.PrintAsync((owner ?? View)?.TryGetPlatformHandle()?.Handle ?? 0,
                result.Png, "RitsukageGif", token);
        }
    }
}
