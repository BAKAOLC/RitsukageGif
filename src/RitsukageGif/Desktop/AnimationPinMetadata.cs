using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationPinMetadata
    {
        private const int MaximumEncodedBytes = 256 * 1024 * 1024;
        private const int MaximumFrames = 100000;
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required string Extension { get; init; }
        public required string MimeType { get; init; }
        public bool IsHdr { get; private init; }
        public int TotalPlays { get; private init; }
        public required IReadOnlyList<AnimationPinFrameInfo> Frames { get; init; }
        private byte[]? PngHeader { get; init; }
        private IReadOnlyList<ReadOnlyMemory<byte>> SharedChunks { get; init; } = [];

        public static AnimationPinMetadata? Read(byte[] ownedEncoded, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ownedEncoded);
            token.ThrowIfCancellationRequested();
            if (ownedEncoded.Length > MaximumEncodedBytes)
                throw new InvalidDataException("Animated image exceeds the encoded size limit.");
            if (ownedEncoded.AsSpan().StartsWith(PngSignature)) return ReadPng(ownedEncoded, token);
            var gif = ownedEncoded.AsSpan().StartsWith("GIF87a"u8) || ownedEncoded.AsSpan().StartsWith("GIF89a"u8);
            var webp = ownedEncoded.Length >= 12 && ownedEncoded.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                                                 && ownedEncoded.AsSpan(8, 4).SequenceEqual("WEBP"u8);
            if (!gif && !webp) return null;
            using var stream = new MemoryStream(ownedEncoded, false);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid animated image.");
            if (codec.FrameCount <= 1) return null;
            if (codec.FrameCount > MaximumFrames || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                throw new InvalidDataException("Animated image geometry exceeds the limit.");
            var frames = new List<AnimationPinFrameInfo>(codec.FrameCount);
            foreach (var frame in codec.FrameInfo)
            {
                token.ThrowIfCancellationRequested();
                frames.Add(new(Math.Max(1, checked(frame.Duration * TimeSpan.TicksPerMillisecond)),
                    new(0, 0, codec.Info.Width, codec.Info.Height), frame.DisposalMethod switch
                    {
                        SKCodecAnimationDisposalMethod.RestorePrevious => 2,
                        SKCodecAnimationDisposalMethod.RestoreBackgroundColor => 1,
                        _ => 0,
                    }));
            }

            if (frames.Count != codec.FrameCount)
                throw new InvalidDataException("Animated image frame metadata is incomplete.");
            return new()
            {
                Width = codec.Info.Width, Height = codec.Info.Height, Extension = gif ? ".gif" : ".webp",
                MimeType = gif ? "image/gif" : "image/webp", Frames = frames,
                TotalPlays = codec.RepetitionCount < 0 ? 0 : checked(codec.RepetitionCount + 1),
            };
        }
    }
}
