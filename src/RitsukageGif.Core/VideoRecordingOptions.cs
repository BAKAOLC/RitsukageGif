namespace RitsukageGif.Core
{
    public sealed record VideoRecordingOptions(RecordingOptions Capture)
    {
        public VideoContainer Container { get; init; }
        public OutputColorMode ColorMode { get; init; }
        public HdrToneMapMode ToneMap { get; init; }
        public int Quality { get; init; } = 23;
        public bool Overwrite { get; init; } = true;
        public IReadOnlyList<VideoAudioInput> AudioInputs { get; init; } = [];

        public IReadOnlyList<string> GetOutputPaths(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return ColorMode == OutputColorMode.Both
                ?
                [
                    fullPath, Path.Combine(Path.GetDirectoryName(fullPath)!,
                        Path.GetFileNameWithoutExtension(fullPath) + ".hdr" + Path.GetExtension(fullPath)),
                ]
                : [fullPath];
        }

        public void Validate()
        {
            ArgumentNullException.ThrowIfNull(Capture);
            ArgumentNullException.ThrowIfNull(AudioInputs);
            Capture.Validate();
            if (!Enum.IsDefined(ColorMode) || !Enum.IsDefined(ToneMap))
                throw new ArgumentOutOfRangeException(nameof(ColorMode));
            if (!Enum.IsDefined(Container) || Quality is < 0 or > 51 || AudioInputs.Count > 8)
                throw new ArgumentOutOfRangeException(nameof(Quality));
            foreach (var input in AudioInputs)
            {
                ArgumentNullException.ThrowIfNull(input);
                ArgumentException.ThrowIfNullOrWhiteSpace(input.Input);
            }
        }
    }
}
