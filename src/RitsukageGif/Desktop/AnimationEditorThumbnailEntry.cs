using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationEditorThumbnailEntry
    {
        public required string Path { get; init; }

        public TaskCompletionSource<Bitmap?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Bitmap?> Loading => Completion.Task;
        public bool Started { get; set; }
        public bool PrefetchOnly { get; set; }
        public Bitmap? Bitmap { get; set; }
        public int Leases { get; set; }
        public long Stamp { get; set; }
        public bool Retired { get; set; }
    }
}
