namespace RitsukageGif.Scripting
{
    public sealed record PresetRecordingSettings
    {
        public int? Fps { get; init; }
        public int? Scale { get; init; }
        public OutputFormat? Format { get; init; }
        public GifQuality? Quality { get; init; }
        public bool? Cursor { get; init; }
        public bool? Sound { get; init; }
        public bool? Ffmpeg { get; init; }
        public bool? Copy { get; init; }

        public PresetRecordingSettings Overlay(PresetRecordingSettings other) => new()
        {
            Fps = other.Fps ?? Fps, Scale = other.Scale ?? Scale, Format = other.Format ?? Format,
            Quality = other.Quality ?? Quality, Cursor = other.Cursor ?? Cursor, Sound = other.Sound ?? Sound,
            Ffmpeg = other.Ffmpeg ?? Ffmpeg, Copy = other.Copy ?? Copy
        };
    }
}
