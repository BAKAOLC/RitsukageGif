using System.Net;

namespace RitsukageGif.Desktop
{
    internal static class ApplicationHttp
    {
        private static readonly Lock Gate = new();
        private static NetworkPreferences _preferences = new();
        private static string? _password;
        private static readonly HashSet<NetworkRoutingHandler> Handlers = [];

        // ReSharper disable once MemberCanBePrivate.Global
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        internal static Uri VerificationDestination { get; set; } = new("https://api.github.com/");

        internal static HttpClient CreateClient(bool allowAutoRedirect = true)
        {
            lock (Gate)
            {
                var handler = new NetworkRoutingHandler(allowAutoRedirect, _preferences, _password);
                Handlers.Add(handler);
                return new(handler);
            }
        }

        internal static void Configure(NetworkPreferences preferences, string? password = null)
        {
            preferences = preferences.Normalize();
            preferences.Validate();
            lock (Gate)
            {
                if (preferences == _preferences && password is null) return;
                _preferences = preferences;
                _password = password;
                foreach (var handler in Handlers) handler.ChangeRoute(preferences, password);
            }
        }

        internal static void Remove(NetworkRoutingHandler handler)
        {
            lock (Gate) Handlers.Remove(handler);
        }

        internal static async Task<SocketsHttpHandler> CreateHandlerAsync(NetworkPreferences preferences,
            string? password, bool allowAutoRedirect, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (preferences.Proxy != NetworkProxyKind.None && preferences.HasPassword && password is null)
                password = await NetworkCredentialStore.Read(preferences, token).ConfigureAwait(false)
                           ?? throw new InvalidOperationException("The proxy password is unavailable.");
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = allowAutoRedirect,
                UseProxy = preferences.Proxy != NetworkProxyKind.None,
                ConnectTimeout = TimeSpan.FromSeconds(15),
            };
            if (!handler.UseProxy) return handler;
            handler.Proxy = new WebProxy(preferences.Address)
            {
                Credentials = preferences.HasPassword || preferences.Username.Length > 0 ||
                              !string.IsNullOrEmpty(password)
                    ? new NetworkCredential(preferences.Username, password ?? "")
                    : null,
            };
            return handler;
        }

        internal static async Task VerifyAsync(NetworkPreferences preferences, string? password,
            CancellationToken token, Uri? destination = null)
        {
            preferences.Validate();
            using var handler = await CreateHandlerAsync(preferences, password, false, token).ConfigureAwait(false);
            // ReSharper disable once ShortLivedHttpClient
            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(15);
            using var request = new HttpRequestMessage(HttpMethod.Get, destination ?? VerificationDestination);
            request.Headers.UserAgent.ParseAdd("RitsukageGif");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
    }
}
