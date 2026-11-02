namespace RitsukageGif.Desktop
{
    internal sealed class NetworkRoutingHandler(
        bool allowAutoRedirect,
        NetworkPreferences preferences,
        string? password)
        : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private NetworkRouteGeneration? _current = new(preferences, password, allowAutoRedirect);

        internal void ChangeRoute(NetworkPreferences value, string? secret)
        {
            NetworkRouteGeneration? previous;
            lock (_gate)
            {
                if (_current is null) return;
                previous = _current;
                _current = new(value, secret, allowAutoRedirect);
            }

            previous.Release();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken token)
        {
            NetworkRouteGeneration route;
            lock (_gate)
            {
                route = _current ?? throw new ObjectDisposedException(nameof(NetworkRoutingHandler));
                route.Acquire();
            }

            try
            {
                var response = await route.SendAsync(request, token).ConfigureAwait(false);
                response.Content = new NetworkLeasedContent(response.Content, route);
                return response;
            }
            catch
            {
                route.Release();
                throw;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ApplicationHttp.Remove(this);
                NetworkRouteGeneration? previous;
                lock (_gate)
                {
                    previous = _current;
                    _current = null;
                }

                previous?.Release();
            }

            base.Dispose(disposing);
        }
    }
}
