using System.Buffers;
using System.Diagnostics;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TranslationService(HttpClient? client = null)
    {
        private const int MaximumInputLength = 100_000;
        private const int MaximumResponseBytes = 2 * 1024 * 1024;

        private static readonly HttpClient SharedClient = new(new SocketsHttpHandler { AllowAutoRedirect = false })
            { Timeout = Timeout.InfiniteTimeSpan };

        private static readonly ILogger Log = LogHub.GetLogger("Desktop.Translation");
        private readonly HttpClient _client = client ?? SharedClient;

        public async Task<TranslationResult> TranslateAsync(TranslationOptions options, TranslationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            Validate(options, request);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            using var message = CreateRequest(options, request);
            var started = Stopwatch.GetTimestamp();
            Log.ZLogDebug(
                $"Translation started: provider={options.Provider.ToString()}, inputCharacters={request.Text.Length}");
            try
            {
                using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.ZLogWarning(
                        $"Translation HTTP failure: provider={options.Provider.ToString()}, status={(int)response.StatusCode}");
                    throw new TranslationException(TranslationFailureKind.Http,
                        $"Translation service returned HTTP {(int)response.StatusCode}.", response.StatusCode);
                }

                if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                    throw new TranslationException(TranslationFailureKind.ResponseTooLarge,
                        "Translation response exceeds the size limit.");
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
                try
                {
                    while (true)
                    {
                        var read = await stream.ReadAsync(rented.AsMemory(0,
                                (int)Math.Min(rented.Length, MaximumResponseBytes + 1 - buffer.Length)), timeout.Token)
                            .ConfigureAwait(false);
                        if (read == 0) break;
                        if (buffer.Length + read > MaximumResponseBytes)
                            throw new TranslationException(TranslationFailureKind.ResponseTooLarge,
                                "Translation response exceeds the size limit.");
                        buffer.Write(rented, 0, read);
                    }

                    timeout.Token.ThrowIfCancellationRequested();
                    using var json = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length),
                        new() { MaxDepth = 32 });
                    var result = ReadResult(options.Provider, json.RootElement);
                    Log.ZLogInformation(
                        $"Translation completed: provider={options.Provider.ToString()}, outputCharacters={result.Text.Length}, elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds}");
                    return result;
                }
                catch (JsonException)
                {
                    throw new TranslationException(TranslationFailureKind.InvalidResponse,
                        "Translation service returned invalid JSON.");
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented, true);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                     timeout.IsCancellationRequested)
            {
                throw new TimeoutException("Translation request timed out.");
            }
        }

        private static void Validate(TranslationOptions options, TranslationRequest request)
        {
            if (!Enum.IsDefined(options.Provider)) throw new ArgumentException("Unknown translation provider.");
            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException(
                    "Translation endpoint must be an absolute HTTP or HTTPS URL without user information or fragment.");
            if (options.TimeoutSeconds is < 1 or > 300)
                throw new ArgumentOutOfRangeException(nameof(options),
                    "Translation timeout must be between 1 and 300 seconds.");
            if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaximumInputLength)
                throw new ArgumentException("Translation text must contain between 1 and 100000 characters.");
            if (!ValidLanguage(request.SourceLanguage) || !ValidLanguage(request.TargetLanguage) ||
                request.TargetLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Translation source and target must be valid language identifiers.");
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (options.Provider)
            {
                case TranslationProvider.DeepL or TranslationProvider.Azure
                    when string.IsNullOrWhiteSpace(options.ApiKey):
                    throw new ArgumentException("The selected translation provider requires an API key.");
                case TranslationProvider.OpenAiCompatible when string.IsNullOrWhiteSpace(options.Model):
                    throw new ArgumentException("An OpenAI-compatible translation model is required.");
            }
        }

        private static bool ValidLanguage(string? value)
        {
            return value is { Length: > 0 and <= 64 } &&
                   value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
        }
    }
}
