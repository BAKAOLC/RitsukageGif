using RitsukageGif.SettingsGeneration;

namespace RitsukageGif.Desktop
{
    [GenerateSettingsMerge(typeof(AppSettings))]
    public sealed record NetworkPreferences
    {
        public NetworkProxyKind Proxy { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Username { get; set; } = "";
        public string CredentialId { get; set; } = "";

        internal bool HasPassword => CredentialId.Length > 0;

        internal string CredentialKey => "network/proxy/" + CredentialId;

        internal Uri Address => new UriBuilder(Proxy == NetworkProxyKind.Socks5 ? "socks5" : "http", Host, Port).Uri;

        internal NetworkPreferences Normalize()
        {
            if (Proxy == NetworkProxyKind.None || !Enum.IsDefined(Proxy)) return new();
            var value = this with { Host = Host.Trim() };
            return value.Port is >= 1 and <= 65535 && Uri.CheckHostName(value.Host) != UriHostNameType.Unknown &&
                   (value.CredentialId.Length == 0 || Guid.TryParseExact(value.CredentialId, "N", out _))
                ? value
                : new();
        }

        internal void Validate()
        {
            if (!Enum.IsDefined(Proxy)) throw new ArgumentException("The proxy type is invalid.");
            if (CredentialId.Length > 0 && !Guid.TryParseExact(CredentialId, "N", out _))
                throw new ArgumentException("The proxy credential identifier is invalid.");
            if (Proxy == NetworkProxyKind.None) return;
            if (Port is < 1 or > 65535 || Uri.CheckHostName(Host) == UriHostNameType.Unknown)
                throw new ArgumentException("The proxy host or port is invalid.");
        }
    }
}
