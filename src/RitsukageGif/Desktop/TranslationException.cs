using System.Net;

namespace RitsukageGif.Desktop
{
    internal sealed class TranslationException(
        TranslationFailureKind kind,
        string message,
        HttpStatusCode? statusCode = null) : Exception(message)
    {
        public TranslationFailureKind Kind { get; } = kind;
        public HttpStatusCode? StatusCode { get; } = statusCode;
    }
}
