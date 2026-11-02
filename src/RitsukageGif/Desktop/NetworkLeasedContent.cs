using System.Net;

namespace RitsukageGif.Desktop
{
    internal sealed class NetworkLeasedContent : HttpContent
    {
        private readonly HttpContent _inner;
        private NetworkRouteGeneration? _route;

        internal NetworkLeasedContent(HttpContent inner, NetworkRouteGeneration route)
        {
            _inner = inner;
            _route = route;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return _inner.CopyToAsync(stream);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken token)
        {
            return _inner.CopyToAsync(stream, token);
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return CreateContentReadStreamAsync(CancellationToken.None);
        }

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken token)
        {
            return new NetworkLeasedStream(await _inner.ReadAsStreamAsync(token).ConfigureAwait(false), this);
        }

        internal void ReleaseRoute()
        {
            Interlocked.Exchange(ref _route, null)?.Release();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _inner.Headers.ContentLength ?? 0;
            return _inner.Headers.ContentLength.HasValue;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                try
                {
                    _inner.Dispose();
                }
                finally
                {
                    ReleaseRoute();
                }

            base.Dispose(disposing);
        }
    }
}
