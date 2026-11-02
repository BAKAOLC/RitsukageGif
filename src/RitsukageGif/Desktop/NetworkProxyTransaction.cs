namespace RitsukageGif.Desktop
{
    internal static class NetworkProxyTransaction
    {
        private static readonly SemaphoreSlim Gate = new(1);
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Network");

        internal static async Task<NetworkPreferences> CommitAsync(Func<NetworkPreferences> current,
            NetworkPreferences preferences, string password,
            Func<NetworkPreferences, CancellationToken, Task> persist, CancellationToken token)
        {
            preferences.Validate();
            await Gate.WaitAsync(token);
            try
            {
                var previous = current();
                await RecoverCoreAsync(previous);
                var candidate = preferences with
                {
                    CredentialId = password.Length > 0 ? Guid.NewGuid().ToString("N") : "",
                };
                var hasJournal = previous.HasPassword || candidate.HasPassword;
                if (hasJournal) NetworkProxyJournal.Write(previous.CredentialId, candidate.CredentialId);
                try
                {
                    if (candidate.HasPassword) await NetworkCredentialStore.Write(candidate, password, token);
                    token.ThrowIfCancellationRequested();
                    await persist(candidate, token);
                }
                catch
                {
                    if (hasJournal) await TryRecoverAsync(current);
                    throw;
                }

                if (hasJournal) await TryRecoverAsync(current);
                return candidate;
            }
            finally
            {
                Gate.Release();
            }
        }

        internal static async Task RecoverAsync(NetworkPreferences active)
        {
            await Gate.WaitAsync();
            try
            {
                await TryRecoverAsync(() => active);
            }
            finally
            {
                Gate.Release();
            }
        }

        private static async Task TryRecoverAsync(Func<NetworkPreferences> current)
        {
            try
            {
                await RecoverCoreAsync(current());
            }
            catch (Exception error)
            {
                Logger.ZLogWarning($"Network credential recovery deferred. ExceptionType={error.GetType().Name}");
            }
        }

        private static async Task RecoverCoreAsync(NetworkPreferences active)
        {
            if (NetworkProxyJournal.Read() is not { } pending) return;
            foreach (var id in new[] { pending.Previous, pending.Candidate })
                if (id.Length > 0 && id != active.CredentialId)
                    await NetworkCredentialStore.Delete(new() { CredentialId = id }, CancellationToken.None);
            NetworkProxyJournal.Clear();
        }
    }
}
