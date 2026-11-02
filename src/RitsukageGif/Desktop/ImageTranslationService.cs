namespace RitsukageGif.Desktop
{
    internal sealed class ImageTranslationService(TranslationService? translation = null)
    {
        private readonly TranslationService _translation = translation ?? new();

        internal static async Task<OcrRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> png,
            TranslationPreferences preferences, RecognitionPreferences recognition, ITextRecognitionService ocr,
            CancellationToken token)
        {
            var capabilities = await ocr.GetCapabilitiesAsync(token).ConfigureAwait(false);
            var language = RecognitionOutput.Language(recognition.Normalize(), capabilities.Languages);
            if (!preferences.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
                language = capabilities.Languages.FirstOrDefault(candidate =>
                    SameLanguage(candidate.Tag, preferences.SourceLanguage))?.Tag ?? language;
            var result = await ocr.RecognizeImageAsync(png,
                language, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return preferences.Mode == TranslationMode.ImageToText
                ? result with { Text = RecognitionTextFormatter.Format(result, recognition) }
                : result;
        }

        internal async Task<ImageTranslationDocument> TranslateRecognizedAsync(ReadOnlyMemory<byte> png,
            OcrRecognitionResult recognized, TranslationPreferences preferences, string apiKey,
            CancellationToken token)
        {
            preferences = preferences.Normalize();
            var source = preferences.SourceLanguage;
            var target = preferences.TargetLanguage;
            if (preferences.Mode == TranslationMode.ImageToText && preferences.FallbackLanguage.Length != 0 &&
                !source.Equals("auto", StringComparison.OrdinalIgnoreCase) && SameLanguage(source, target))
                target = preferences.FallbackLanguage;
            var options = preferences.CreateOptions(apiKey);
            if (preferences.Mode == TranslationMode.ImageToText)
            {
                var translated = await _translation.TranslateAsync(options,
                    new(recognized.Text, target, source), token).ConfigureAwait(false);
                if (preferences.FallbackLanguage.Length != 0 && !SameLanguage(target, preferences.FallbackLanguage) &&
                    translated.DetectedSourceLanguage is { Length: > 0 } detected && SameLanguage(detected, target))
                {
                    target = preferences.FallbackLanguage;
                    translated = await _translation.TranslateAsync(options,
                        new(recognized.Text, target, source), token).ConfigureAwait(false);
                }

                return new(png, recognized.Width, recognized.Height,
                    translated.DetectedSourceLanguage ?? source, target,
                    translated.Text, []);
            }

            var lines = recognized.Lines.Where(line => !string.IsNullOrWhiteSpace(line.Text)).ToArray();
            if (lines.Length == 0) throw new InvalidDataException("No text regions were detected in the image.");
            var blocks = new ImageTranslationBlock[lines.Length];
            var detectedLanguages = new string?[lines.Length];
            await Parallel.ForEachAsync(Enumerable.Range(0, lines.Length),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
                async (index, cancellation) =>
                {
                    var line = lines[index];
                    var bounds = Clip(line.Bounds, recognized.Width, recognized.Height);
                    var translated = await _translation.TranslateAsync(options,
                        new(line.Text, target, source), cancellation).ConfigureAwait(false);
                    detectedLanguages[index] = translated.DetectedSourceLanguage;
                    var words = line.Words.Where(word => !string.IsNullOrWhiteSpace(word.Text))
                        .Select(word => Clip(word.Bounds, recognized.Width, recognized.Height)).ToArray();
                    blocks[index] = new(index, bounds, words, translated.Text);
                }).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var detectedSources = detectedLanguages.Where(language => !string.IsNullOrWhiteSpace(language) &&
                                                                      !language.Equals("auto",
                                                                          StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var detectedSource = detectedSources.Length == 1 ? detectedSources[0] ?? source : source;
            return new(png, recognized.Width, recognized.Height, detectedSource, target,
                string.Join(Environment.NewLine, blocks.Select(block => block.TranslatedText)), blocks);
        }

        private static OcrBounds Clip(OcrBounds value, int width, int height)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Width) ||
                !float.IsFinite(value.Height) || value.Width <= 0 || value.Height <= 0)
                throw new InvalidDataException("The recognized text region is invalid.");
            var left = Math.Clamp(value.X, 0, width);
            var top = Math.Clamp(value.Y, 0, height);
            var right = Math.Clamp(value.X + value.Width, left, width);
            var bottom = Math.Clamp(value.Y + value.Height, top, height);
            if (right <= left || bottom <= top)
                throw new InvalidDataException("The recognized text region is outside the image.");
            return new(left, top, right - left, bottom - top);
        }

        private static bool SameLanguage(string first, string second)
        {
            if (first.Equals(second, StringComparison.OrdinalIgnoreCase)) return true;
            var left = first.Split('-', '_');
            var right = second.Split('-', '_');
            if (!left[0].Equals(right[0], StringComparison.OrdinalIgnoreCase)) return false;
            var leftScript = left.FirstOrDefault(part => part.Length == 4);
            var rightScript = right.FirstOrDefault(part => part.Length == 4);
            if (left[0].Equals("zh", StringComparison.OrdinalIgnoreCase) && leftScript is null && rightScript is null)
                return false;
            return string.Equals(leftScript, rightScript, StringComparison.OrdinalIgnoreCase);
        }
    }
}
