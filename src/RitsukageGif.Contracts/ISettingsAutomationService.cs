namespace RitsukageGif.Contracts
{
    public interface ISettingsAutomationService
    {
        Task<string> GetAsync(string pointer, CancellationToken token = default);

        Task SetAsync(string pointer, string valueJson, SettingsAutomationOptions? options = null,
            CancellationToken token = default);

        Task ImportAsync(string path, SettingsAutomationOptions? options = null, CancellationToken token = default);
        Task<string> ExportAsync(string path, CancellationToken token = default);
        Task ResetAsync(string pointer, SettingsAutomationOptions? options = null, CancellationToken token = default);
    }
}
