namespace RitsukageGif.Core
{
    public sealed partial class MediaProcessingService
    {
        private static MediaProcessResult[] ProcessSelectedFrames(MediaInfo info,
            MediaProcessOutput[] outputs, CancellationToken token)
        {
            var results = new MediaProcessResult[outputs.Length];
            // The public batch entry point limits outputs to 16; track initialized entries without nullable results.
            var completed = 0;
            // Canvas, current frame, previous canvas, yielded frame, prepared frame and output transform.
            CheckBudget(info.Width, info.Height, 3);
            var ordered = Enumerable.Range(0, outputs.Length)
                .OrderBy(index => outputs[index].Options.FrameIndex).ToArray();
            var position = 0;
            foreach (var index in ordered)
                if (outputs[index].Options.FrameIndex < 0 || outputs[index].Options.FrameIndex >= info.Frames)
                {
                    results[index] = new(null, new ArgumentOutOfRangeException(nameof(outputs)));
                    completed |= 1 << index;
                }

            var rejected = completed;
            ordered = [.. ordered.Where(index => (rejected & (1 << index)) == 0)];
            if (ordered.Length == 0) return results;

            try
            {
                var frameIndex = 0;
                foreach (var frame in MediaPngCodec.ReadCoalescedFrames(info.Path, info, token))
                {
                    if (frameIndex == outputs[ordered[position]].Options.FrameIndex)
                    {
                        using var prepared = new PreparedMediaInput(info, frameIndex, frame);
                        while (position < ordered.Length &&
                               outputs[ordered[position]].Options.FrameIndex == frameIndex)
                        {
                            token.ThrowIfCancellationRequested();
                            var index = ordered[position++];
                            var output = outputs[index];
                            try
                            {
                                results[index] = new(ProcessWithDiagnostics(info.Path, Path.GetFullPath(output.Path),
                                    output.Options,
                                    token, prepared), null);
                            }
                            catch (Exception error) when (error is not OperationCanceledException)
                            {
                                token.ThrowIfCancellationRequested();
                                results[index] = new(null, error);
                            }

                            completed |= 1 << index;
                        }

                        if (position == ordered.Length) break;
                    }

                    frameIndex++;
                }

                if (position != ordered.Length) throw new InvalidDataException();
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Logger.ZLogError(error,
                    $"Selected-frame batch decoding failed: outputs={outputs.Length}, exceptionType={error.GetType().Name}, hResult={error.HResult}");
                token.ThrowIfCancellationRequested();
                foreach (var index in ordered)
                    if ((completed & (1 << index)) == 0)
                        results[index] = new(null, error);
            }

            return results;
        }
    }
}
