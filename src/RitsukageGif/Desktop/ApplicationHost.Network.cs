namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly SemaphoreSlim _networkPersistenceGate = new(1);
        private NetworkPreferences _committedNetwork = new();
        private Task _networkRecovery = Task.CompletedTask;

        private void InitializeNetworkPreferences()
        {
            _committedNetwork = Settings.Network;
            ApplicationHttp.Configure(_committedNetwork);
            if (!IsTaskHost) _networkRecovery = TryRecoverNetworkPreferencesAsync();
        }

        private async Task TryRecoverNetworkPreferencesAsync()
        {
            try
            {
                await RecoverNetworkPreferencesAsync();
            }
            catch (Exception error)
            {
                SettingsLogger.ZLogWarning($"Network settings recovery deferred. ExceptionType={error.GetType().Name}");
            }
        }

        private async Task RecoverNetworkPreferencesAsync()
        {
            if (IsTaskHost || !NetworkProxyJournal.HasPending) return;
            var persisted = AppSettings.ReadPersistedNetwork();
            if (persisted != Volatile.Read(ref _committedNetwork))
            {
                Volatile.Write(ref _committedNetwork, persisted);
                Settings = Settings with { Network = persisted };
                ApplicationHttp.Configure(persisted);
                _settingsWindow?.RefreshNetworkPreferences(persisted);
                Notify();
            }

            await NetworkProxyTransaction.RecoverAsync(persisted);
        }

        private async Task<NetworkPreferences> CommitNetworkPreferencesAsync(NetworkPreferences preferences,
            string password, CancellationToken token)
        {
            if (IsTaskHost)
            {
                token.ThrowIfCancellationRequested();
                ApplyTaskSettingsAutomation(Settings with { Network = preferences },
                    new() { ProxyPassword = password }, false);
                _settingsWindow?.RefreshNetworkPreferences(Settings.Network);
                return Settings.Network;
            }

            await _networkRecovery;
            await RecoverNetworkPreferencesAsync();
            return await NetworkProxyTransaction.CommitAsync(AppSettings.ReadPersistedNetwork, preferences,
                password, async (candidate, cancellationToken) =>
                {
                    await _networkPersistenceGate.WaitAsync(cancellationToken);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var saved = Settings.Snapshot() with { Network = candidate };
                        // The atomic settings writer is the commit point; cancellation cannot undo a completed write.
                        try
                        {
                            await Task.Run(() => PersistSettingsCore(saved), CancellationToken.None);
                        }
                        catch (Exception error)
                        {
                            SettingsLogger.ZLogWarning(
                                $"Network settings persistence failed; confirming commit. ExceptionType={error.GetType().Name}");
                            if (AppSettings.ReadPersistedNetwork() != candidate) throw;
                        }

                        Volatile.Write(ref _committedNetwork, candidate);
                        Settings = Settings with { Network = candidate };
                        ApplicationHttp.Configure(candidate, password);
                        _settingsWindow?.RefreshNetworkPreferences(candidate);
                        Notify();
                    }
                    finally
                    {
                        _networkPersistenceGate.Release();
                    }
                }, token);
        }
    }
}
