using System.Numerics;

namespace RitsukageGif.Core
{
    public sealed record AudioCaptureFormat(
        int SampleRate,
        int Channels,
        int BitsPerSample,
        bool IsFloat,
        uint ChannelMask = 0)
    {
        public string InputFormat => (IsFloat, BitsPerSample) switch
        {
            (true, 32) => "f32le",
            (true, 64) => "f64le",
            (false, 8) => "u8",
            (false, 16) => "s16le",
            (false, 24) => "s24le",
            (false, 32) => "s32le",
            _ => throw new ArgumentOutOfRangeException(nameof(BitsPerSample)),
        };

        public void Validate()
        {
            if (SampleRate is < 8000 or > 384000 || Channels is < 1 or > 32)
                throw new ArgumentOutOfRangeException(nameof(SampleRate));
            if (ChannelMask != 0 && ((ChannelMask & ~0x3ffffu) != 0 || BitOperations.PopCount(ChannelMask) != Channels))
                throw new ArgumentOutOfRangeException(nameof(ChannelMask));
            _ = InputFormat;
        }
    }
}
