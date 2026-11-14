using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task<bool> ExportScreenshotDocumentAsync(Window? owner, ScreenshotResult result,
            CancellationToken token)
        {
            var profile = ScreenshotSaveForOutput.ManualProfile();
            var context = ScreenshotName(result);
            var name = Path.ChangeExtension(profile.CreateFileName(context), ".pdf");
            var path = await DocumentExportWindow.ShowAsync(owner, result.Png, name, token,
                profile.ResolveDirectory(Settings.OutputDirectory, context));
            if (path is null) return false;
            LastScreenshotOutput = new(OutputColorMode.Sdr, [path]);
            _screenshotInvocation.Value?.AddPaths([path]);
            SetStatus(UiText.Format("AnimationExported", path));
            try
            {
                var completionToken = _screenshotInvocation.Value?.PropagateErrors == true
                    ? token
                    : CancellationToken.None;
                await ScreenshotSaveCompletion.RunAsync([path], profile, _services.Context.Clipboard,
                    _services.Context.StorageProvider,
                    () => CopyScreenshotAsync(result with { ColorMode = OutputColorMode.Sdr, Hdr = null },
                        completionToken),
                    ShowOutputNotification, completionToken);
            }
            catch (Exception error)
            {
                SetStatus(error.Message);
                if (_screenshotInvocation.Value?.PropagateErrors == true) throw;
            }
            finally
            {
                QueueCleanup([path], profile.AfterSave is ScreenshotAfterSaveAction.CopyFile or
                    ScreenshotAfterSaveAction.CopyFileAndPath
                    ? [path]
                    : null);
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
