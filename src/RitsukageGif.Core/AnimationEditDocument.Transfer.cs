namespace RitsukageGif.Core
{
    public sealed partial class AnimationEditDocument
    {
        public static AnimationEditTransfer PrepareImport(string path, string ownedDirectory,
            CancellationToken token = default)
        {
            using var document = new AnimationEditDocument(ownedDirectory, false);
            var frames = document.Read(path, false, token, out var iterations);
            token.ThrowIfCancellationRequested();
            return new([
                .. frames.Select(frame => new AnimationEditTransferFrame(Path.GetFileName(frame.Path),
                    frame.Width, frame.Height, frame.DelayMs)),
            ], iterations);
        }

        public static AnimationEditDocument AdoptTransfer(string ownedDirectory, AnimationEditTransfer transfer)
        {
            if (transfer.Frames is not { Length: > 0 }) throw new InvalidDataException();
            var directory = Path.GetFullPath(ownedDirectory);
            var frames = new AnimationEditFrame[transfer.Frames.Length];
            for (var index = 0; index < frames.Length; index++)
            {
                var frame = transfer.Frames[index];
                if (string.IsNullOrEmpty(frame.FileName) || Path.GetFileName(frame.FileName) != frame.FileName ||
                    frame.FileName is "." or "..")
                    throw new InvalidDataException();
                ValidateSize(frame.Width, frame.Height);
                ValidateDelay(frame.DelayMs);
                var path = Path.Combine(directory, frame.FileName);
                if (!File.Exists(path)) throw new InvalidDataException();
                frames[index] = new(path, frame.Width, frame.Height, frame.DelayMs);
            }

            return new(directory, true) { _current = new(Array.AsReadOnly(frames), transfer.Iterations) };
        }
    }
}
