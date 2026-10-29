namespace RitsukageGif.Desktop
{
    public sealed record VideoRecordingPreferences
    {
        public VideoContainer Container { get; set; }
        public int Fps { get; set; } = 30;
        public int Divisor { get; set; } = 1;
        public int Quality { get; set; } = 23;
        public bool SystemAudio { get; set; }
        public bool Microphone { get; set; }
        public string? SystemDevice { get; set; }
        public string? MicrophoneDevice { get; set; }
        public string OutputDirectory { get; set; } = "";
    }
}
