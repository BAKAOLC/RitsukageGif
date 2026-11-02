namespace RitsukageGif.Core
{
    public static partial class PngAnimation
    {
        private static byte[] CreateFrameProfile(List<(string Type, byte[] Data)> chunks, CancellationToken token)
        {
            if (!chunks.Any(chunk => chunk.Type is "cICP" or "gAMA" or "cHRM")) return [];
            var cicp = chunks.FirstOrDefault(chunk => chunk.Type == "cICP").Data;
            if (cicp is [_, not (1 or 6 or 8 or 13), _, _]) return [];
            var metadata = chunks.Select(chunk => (chunk.Type, (ReadOnlyMemory<byte>)chunk.Data)).ToList();
            using var space = PngColorTransform.CreateSourceSpace(metadata, token);
            return PngColorTransform.CreateRgbProfile(space);
        }
    }
}
