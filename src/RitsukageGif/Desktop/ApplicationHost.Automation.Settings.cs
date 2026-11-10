using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost : ISettingsAutomationService
    {
        async Task<string> ISettingsAutomationService.GetAsync(string pointer, CancellationToken token)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RequireSettingsAutomation(token);
                return SettingsAutomationJson.Read(Settings, pointer);
            });
        }

        async Task ISettingsAutomationService.SetAsync(string pointer, string valueJson,
            SettingsAutomationOptions? options, CancellationToken token)
        {
            var edit = await PrepareSettingsAutomationAsync(settings =>
                SettingsAutomationJson.Write(settings, pointer, valueJson, token), token);
            await ApplySettingsAutomationAsync(edit, options, token);
        }

        async Task ISettingsAutomationService.ImportAsync(string path, SettingsAutomationOptions? options,
            CancellationToken token)
        {
            await using var input = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read,
                FileShare.Read, 16384, true);
            var imported = await SettingsTransfer.ImportAsync(input, token);
            var edit = await PrepareSettingsAutomationAsync(_ => imported, token);
            await ApplySettingsAutomationAsync(edit, options, token);
        }

        async Task<string> ISettingsAutomationService.ExportAsync(string path, CancellationToken token)
        {
            var snapshot = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RequireSettingsAutomation(token);
                return Settings.Snapshot();
            });
            path = Path.GetFullPath(path);
            await SettingsTransfer.ExportAsync(path, snapshot, token);
            return path;
        }

        async Task ISettingsAutomationService.ResetAsync(string pointer, SettingsAutomationOptions? options,
            CancellationToken token)
        {
            var edit = await PrepareSettingsAutomationAsync(settings =>
                SettingsAutomationJson.Reset(settings, pointer, token), token);
            await ApplySettingsAutomationAsync(edit, options, token);
        }

        private async Task<SettingsAutomationEdit> PrepareSettingsAutomationAsync(Func<AppSettings, AppSettings> edit,
            CancellationToken token)
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RequireSettingsAutomation(token);
                _settingsWindow?.CommitAutomationDraft();
                var baseline = Settings.Snapshot();
                return new SettingsAutomationEdit(baseline, edit(baseline));
            });
        }

        private async Task ApplySettingsAutomationAsync(SettingsAutomationEdit edit, SettingsAutomationOptions? options,
            CancellationToken token)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                RequireSettingsAutomation(token);
                var requested =
                    SettingsAutomationJson.Validate(Settings.MergeChanges(edit.Baseline, edit.Edited), token);
                if (IsTaskHost)
                {
                    ApplyTaskSettingsAutomation(requested, options);
                    return;
                }

                var networkCommitted = false;
                if (requested.Network != Settings.Network || options?.ProxyPassword is not null)
                {
                    var previousNetwork = Settings.Network;
                    var candidate = requested.Network with { CredentialId = "" };
                    var password = options?.ProxyPassword;
                    if (password is null && SameSettingsProxy(candidate, previousNetwork) &&
                        previousNetwork.HasPassword)
                        password = await NetworkCredentialStore.Read(previousNetwork, token)
                                   ?? throw new InvalidOperationException("The proxy password is unavailable.");
                    password ??= "";
                    if (candidate.Proxy != NetworkProxyKind.None)
                        await ApplicationHttp.VerifyAsync(candidate, password, token);
                    else if (password.Length > 0)
                        throw new ArgumentException("A proxy password requires an enabled proxy.");
                    RequireSettingsAutomation(token);
                    if (Settings.Network != previousNetwork)
                        throw new InvalidOperationException(
                            "Network settings changed during verification. Retry the settings operation.");
                    var committed = await CommitNetworkPreferencesAsync(candidate, password, token);
                    edit = edit with { Edited = edit.Edited with { Network = committed } };
                    requested = SettingsAutomationJson.Validate(Settings.MergeChanges(edit.Baseline, edit.Edited),
                        token);
                    networkCommitted = true;
                }

                RequireSettingsAutomation(token);
                var previous = Settings;
                UpdateSettings(requested);
                if (networkCommitted || !ReferenceEquals(previous, Settings))
                    _settingsWindow?.RefreshAutomationSettings(Settings.Snapshot());
                await FlushSettingsAsync().WaitAsync(token);
            });
        }

        private static bool SameSettingsProxy(NetworkPreferences left, NetworkPreferences right)
        {
            return left.Proxy == right.Proxy && left.Port == right.Port &&
                   string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
                   left.Username == right.Username;
        }

        private void RequireSettingsAutomation(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsClosing) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
        }
    }
}
