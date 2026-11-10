using System.Text.Json;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task<CommandReply> ExecuteSettingsCommandAsync(string[] args, CancellationToken token)
        {
            try
            {
                return await ExecuteSettingsCommandCoreAsync(args, token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return new(false, CurrentState, error.Message);
            }
        }

        private async Task<CommandReply> ExecuteSettingsCommandCoreAsync(string[] args, CancellationToken token)
        {
            ISettingsAutomationService service = this;
            switch (args)
            {
                case ["show"]:
                case ["get"]:
                    return SettingsReply(await service.GetAsync("", token));
                case ["get", var pointer]:
                    return SettingsReply(await service.GetAsync(pointer, token));
                case ["set", var pointer, var json]:
                    await service.SetAsync(pointer, json, token: token);
                    break;
                case ["import", var path]:
                    await service.ImportAsync(path, token: token);
                    break;
                case ["export", var path]:
                    return new(true, CurrentState, Output: await service.ExportAsync(path, token));
                case ["reset"]:
                    await service.ResetAsync("", token: token);
                    break;
                case ["reset", var pointer]:
                    await service.ResetAsync(pointer, token: token);
                    break;
                default:
                    throw new ArgumentException(
                        "settings show | get [JSON_POINTER] | set JSON_POINTER JSON | import PATH | export PATH | reset [JSON_POINTER]");
            }

            return new(true, CurrentState);
        }

        private CommandReply SettingsReply(string json)
        {
            using var document = JsonDocument.Parse(json);
            return new(true, CurrentState, SettingsValue: document.RootElement.Clone());
        }
    }
}
