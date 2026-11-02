using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowsOcrBackend : IPlatformOcrBackend
    {
        public string Id => "windows";

        public Task<OcrCapabilities> GetCapabilitiesAsync(CancellationToken token)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return WindowsOcrRecognition.GetCapabilities();
                }
                catch (Exception error) when (error is COMException or DllNotFoundException
                                                  or EntryPointNotFoundException)
                {
                    return new(false, 0, [], error.Message);
                }
            }, token);
        }

        public Task<OcrRecognitionResult> RecognizeAsync(byte[] pixels, int width, int height, string? languageTag,
            CancellationToken token)
        {
            return Task.Run(() => WindowsOcrRecognition.Recognize(pixels, width, height, languageTag, token), token);
        }
    }
}
