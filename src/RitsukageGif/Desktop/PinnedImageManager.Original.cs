namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private async Task<PinnedImageOriginal> ReadOriginalAsync(PinnedImageState state, PinnedImageContent current,
            CancellationToken token)
        {
            var original = current;
            if (state.HasOriginalImage)
            {
                var encoded = await ReadOriginalBytesAsync(OriginalPath(state.ImageId, false), token);
                original = await Task.Run(() => PinnedImageContent.Decode(encoded, token), token);
            }

            var sdr = state.HasOriginalSdrImage
                ? await ReadOriginalBytesAsync(OriginalPath(state.ImageId, true), token)
                : original.SdrPng;
            if (sdr is not null)
            {
                var dimensions = PngImageCodec.ReadDimensions(sdr);
                if (dimensions.Width != original.Width || dimensions.Height != original.Height)
                    throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                await Task.Run(() => PngImageCodec.Decode(sdr, token), token);
            }

            var encodedSource = state.HasOriginalEncoded
                ? await ReadOriginalBytesAsync(OriginalEncodedPath(state.ImageId), token)
                : null;
            var animation = encodedSource is null
                ? null
                : await Task.Run(() => AnimationPinMetadata.Read(encodedSource, token) ??
                                       throw new InvalidDataException(UiText.Get("InvalidFrameBuffer")), token);
            return new(original.Png, sdr, original.Hdr) { Encoded = encodedSource, Animation = animation };
        }

        private static async Task<byte[]> ReadOriginalBytesAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > PinnedImageContent.MaximumEncodedBytes)
                throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, token);
            token.ThrowIfCancellationRequested();
            return bytes;
        }

        private string OriginalPath(string id, bool sdr)
        {
            return ImagePath(id) + (sdr ? ".original.sdr.png" : ".original.png");
        }

        private string OriginalEncodedPath(string id)
        {
            return ImagePath(id) + ".original.media";
        }

        private static async Task WriteSessionImageAsync(string target, ReadOnlyMemory<byte> png,
            List<string> createdImages, CancellationToken token)
        {
            var temporary = target + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, png, token);
                File.Move(temporary, target, false);
                createdImages.Add(target);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
