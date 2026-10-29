using System.Globalization;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private GlobalMouseService? _globalMouse;

        private void RegisterGlobalMouse()
        {
            _globalMouse?.Dispose();
            _globalMouse = null;
            if (!_startServices || !GlobalMouseService.Supported || IsClosing || IsConfiguring ||
                GlobalInputCapture.Active) return;
            try
            {
                _globalMouse = new(Settings.GlobalMouseBindings, Settings.GlobalActions,
                    ExecuteGlobalMouseActionAsync, SetStatus,
                    message => SetInputConfigurationStatus(InputConfigurationStatusSource.GlobalMouse, message),
                    () => ClearInputConfigurationStatus(InputConfigurationStatusSource.GlobalMouse));
            }
            catch (Exception error)
            {
                SetInputConfigurationStatus(InputConfigurationStatusSource.GlobalMouse, error.Message);
            }
        }

        private async Task ExecuteGlobalMouseActionAsync(string id, PixelRect? region)
        {
            var action = Settings.GlobalActions.SingleOrDefault(item => item.Id == id);
            if (action is null || !action.Enabled || IsClosing) return;
            if (!AllowsGlobalInput(action.Kind == GlobalActionKind.ToggleGlobalShortcuts)) return;
            var startsInRegion = region is { IsEmpty: false } && !IsRecording &&
                                 action.Kind is GlobalActionKind.StartRecording or GlobalActionKind.ToggleRecording;
            if (startsInRegion)
            {
                if (IsBusy || IsSelecting || IsConfiguring) return;
            }
            else if (!CanExecuteAction(action))
            {
                return;
            }

            if (region is not { IsEmpty: false } bounds)
            {
                await ExecuteActionAsync(action);
                return;
            }

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (action.Kind)
            {
                case GlobalActionKind.Screenshot:
                case GlobalActionKind.ScreenshotAndCopy:
                case GlobalActionKind.ScreenshotAndPin:
                case GlobalActionKind.ScreenshotAndSave:
                case GlobalActionKind.ScreenshotQuickSave:
                case GlobalActionKind.ScreenshotAndCopyText:
                case GlobalActionKind.ScreenshotAndScroll:
                    await ExecuteScreenshotActionAsync(action, bounds);
                    break;
                case GlobalActionKind.SelectRegion:
                case GlobalActionKind.StartRecording:
                case GlobalActionKind.ToggleRecording when !IsRecording:
                    var reply = await ExecuteCommandAsync([
                        "select", bounds.X.ToString(CultureInfo.InvariantCulture),
                        bounds.Y.ToString(CultureInfo.InvariantCulture),
                        bounds.Width.ToString(CultureInfo.InvariantCulture),
                        bounds.Height.ToString(CultureInfo.InvariantCulture),
                    ]);
                    if (!reply.Success)
                    {
                        SetStatus(reply.Message);
                        break;
                    }

                    if (action.Kind is GlobalActionKind.StartRecording or GlobalActionKind.ToggleRecording)
                        await ToggleRecordingAsync();
                    break;
                default:
                    await ExecuteActionAsync(action);
                    break;
            }
        }
    }
}
