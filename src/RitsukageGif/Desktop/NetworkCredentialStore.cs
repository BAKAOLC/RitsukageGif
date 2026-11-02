namespace RitsukageGif.Desktop
{
    internal static class NetworkCredentialStore
    {
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        internal static Func<NetworkPreferences, CancellationToken, ValueTask<string?>> Read { get; set; } =
            (preferences, token) => CredentialVault.ReadAsync(preferences.CredentialKey, token);

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        internal static Func<NetworkPreferences, string, CancellationToken, ValueTask> Write { get; set; } =
            (preferences, value, token) => CredentialVault.WriteAsync(preferences.CredentialKey, value, token);

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        internal static Func<NetworkPreferences, CancellationToken, ValueTask> Delete { get; set; } =
            (preferences, token) => CredentialVault.DeleteAsync(preferences.CredentialKey, token);
    }
}
