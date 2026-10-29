using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace RitsukageGif.Core
{
    public sealed partial class OcrPrivacyCandidateProvider(
        ITextRecognitionService recognition,
        string? languageTag = null)
        : IPrivacyCandidateProvider
    {
        public string Id => "ocr";

        public async Task<IReadOnlyList<PrivacyCandidate>> DetectAsync(CapturedFrame frame,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(recognition);
            PrivacyDetectionService.ValidateFrame(frame);
            token.ThrowIfCancellationRequested();
            var straight = frame.AlphaMode == CaptureAlphaMode.Straight;
            var pixels = straight ? frame.Pixels : CapturedFramePixels.ToStraightBgra(frame, token);
            var stride = straight ? frame.Stride : checked(frame.Width * 4);
            var recognized = await recognition.RecognizeBgraAsync(pixels, frame.Width, frame.Height,
                stride, languageTag, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (recognized.Width != frame.Width || recognized.Height != frame.Height)
                throw new InvalidOperationException("OCR coordinates do not match the supplied frame.");
            return await Task.Run(() => Classify(recognized, token), token).ConfigureAwait(false);
        }

        internal OcrPrivacyCandidateProvider WithLanguage(string? value)
        {
            return new(recognition, value);
        }

        private ReadOnlyCollection<PrivacyCandidate> Classify(OcrRecognitionResult recognized,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(recognized);
            token.ThrowIfCancellationRequested();
            if (recognized.Width <= 0 || recognized.Height <= 0 ||
                (long)recognized.Width * recognized.Height > 16 * 1024 * 1024)
                throw new ArgumentException("Invalid OCR dimensions.", nameof(recognized));
            var result = new List<PrivacyCandidate>();
            foreach (var line in recognized.Lines.Take(4096))
            {
                token.ThrowIfCancellationRequested();
                if (result.Count >= 4096) break;
                if (line.Text.Length > 16384) continue;
                var sensitive = new List<PixelRect>();
                AddMatches(EmailPattern(), PrivacyCandidateKind.Email);
                AddMatches(IdentityPattern(), PrivacyCandidateKind.IdentityNumber);
                AddMatches(CardPattern(), PrivacyCandidateKind.PaymentCard);
                AddMatches(PhonePattern(), PrivacyCandidateKind.Phone);
                foreach (var word in line.Words.Take(4096))
                {
                    if (result.Count >= 4096) break;
                    var bounds = Clip(word.Bounds, recognized.Width, recognized.Height);
                    if (bounds.IsEmpty || sensitive.Any(region => !region.Intersect(bounds).IsEmpty)) continue;
                    result.Add(new(Id, PrivacyCandidateKind.Text, bounds, word.Text));
                }

                if (line.Words.Count == 0 && sensitive.Count == 0)
                {
                    var bounds = Clip(line.Bounds, recognized.Width, recognized.Height);
                    if (!bounds.IsEmpty) result.Add(new(Id, PrivacyCandidateKind.Text, bounds, line.Text));
                }

                continue;

                void AddMatches(Regex pattern, PrivacyCandidateKind kind)
                {
                    foreach (Match match in pattern.Matches(line.Text))
                    {
                        token.ThrowIfCancellationRequested();
                        if (result.Count >= 4096) return;
                        if ((kind == PrivacyCandidateKind.IdentityNumber && !ValidIdentity(match.ValueSpan)) ||
                            (kind == PrivacyCandidateKind.PaymentCard && !ValidCard(match.ValueSpan))) continue;
                        var bounds = MatchBounds(line, match.Index, match.Length, recognized.Width, recognized.Height);
                        if (bounds.IsEmpty || sensitive.Any(region => !region.Intersect(bounds).IsEmpty)) continue;
                        sensitive.Add(bounds);
                        result.Add(new(Id, kind, bounds, match.Value));
                    }
                }
            }

            return result.AsReadOnly();
        }

        private static PixelRect MatchBounds(OcrLine line, int start, int length, int width, int height)
        {
            PixelRect? union = null;
            var offset = 0;
            foreach (var word in line.Words.Take(4096))
            {
                var index = line.Text.IndexOf(word.Text, offset, StringComparison.Ordinal);
                if (index < 0) return Clip(line.Bounds, width, height);
                offset = index + word.Text.Length;
                if (offset <= start || index >= start + length) continue;
                var bounds = Clip(word.Bounds, width, height);
                if (bounds.IsEmpty) continue;
                if (union is not { } previous)
                {
                    union = bounds;
                }
                else
                {
                    var left = Math.Min(previous.X, bounds.X);
                    var top = Math.Min(previous.Y, bounds.Y);
                    union = new(left, top, Math.Max(previous.Right, bounds.Right) - left,
                        Math.Max(previous.Bottom, bounds.Bottom) - top);
                }
            }

            return union ?? Clip(line.Bounds, width, height);
        }

        private static PixelRect Clip(OcrBounds bounds, int width, int height)
        {
            if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Width) ||
                !float.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0) return default;
            var left = (int)Math.Clamp(Math.Floor(bounds.X), 0, width);
            var top = (int)Math.Clamp(Math.Floor(bounds.Y), 0, height);
            var right = (int)Math.Clamp(Math.Ceiling((double)bounds.X + bounds.Width), 0, width);
            var bottom = (int)Math.Clamp(Math.Ceiling((double)bounds.Y + bounds.Height), 0, height);
            return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }
    }
}
