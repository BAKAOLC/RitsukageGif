namespace RitsukageGif.Contracts
{
    public sealed record VideoCaptureOptions
    {
        public string Format { get; init; } = "mp4";
        public OutputColorMode? ColorMode { get; init; }
        public HdrToneMapMode? ToneMap { get; init; }
        public int? Fps { get; init; }
        public int? Divisor { get; init; }
        public bool? IncludeCursor { get; init; }
        public int Quality { get; init; } = 23;
        public bool Overwrite { get; init; }
        public bool SystemAudio { get; init; }
        public string? SystemDevice { get; init; }
        public bool Microphone { get; init; }
        public string? MicrophoneDevice { get; init; }
        public IReadOnlyList<VideoCaptureAudioInput> AudioInputs { get; init; } = [];
    }
}
