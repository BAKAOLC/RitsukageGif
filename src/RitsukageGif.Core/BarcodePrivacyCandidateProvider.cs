namespace RitsukageGif.Core
{
    public sealed class BarcodePrivacyCandidateProvider(IBarcodeRecognitionService recognition)
        : IPrivacyCandidateProvider
    {
        public string Id => "qr";

        public async Task<IReadOnlyList<PrivacyCandidate>> DetectAsync(CapturedFrame frame,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(recognition);
            PrivacyDetectionService.ValidateFrame(frame);
            token.ThrowIfCancellationRequested();
            var codes = await recognition.RecognizeAsync(frame, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var result = new List<PrivacyCandidate>();
            foreach (var code in codes)
            {
                token.ThrowIfCancellationRequested();
                if (code.Format != "QR_CODE" || code.Points.Count < 3 ||
                    code.Points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y))) continue;
                // QR results contain bottom-left, top-left and top-right finder centers. Include the missing corner.
                var first = code.Points[0];
                var second = code.Points[1];
                var third = code.Points[2];
                var fourth = new BarcodeRecognitionPoint(first.X + third.X - second.X, first.Y + third.Y - second.Y);
                var left = Math.Min(Math.Min(first.X, second.X), Math.Min(third.X, fourth.X));
                var top = Math.Min(Math.Min(first.Y, second.Y), Math.Min(third.Y, fourth.Y));
                var right = Math.Max(Math.Max(first.X, second.X), Math.Max(third.X, fourth.X));
                var bottom = Math.Max(Math.Max(first.Y, second.Y), Math.Max(third.Y, fourth.Y));
                var padding = Math.Max(4, Math.Max(right - left, bottom - top) / 4);
                var x = (int)Math.Clamp(Math.Floor(left - padding), 0, frame.Width);
                var y = (int)Math.Clamp(Math.Floor(top - padding), 0, frame.Height);
                var x2 = (int)Math.Clamp(Math.Ceiling(right + padding), 0, frame.Width);
                var y2 = (int)Math.Clamp(Math.Ceiling(bottom + padding), 0, frame.Height);
                if (x2 > x && y2 > y)
                    result.Add(new(Id, PrivacyCandidateKind.QrCode, new(x, y, x2 - x, y2 - y), code.Text, true));
            }

            return result.AsReadOnly();
        }
    }
}
