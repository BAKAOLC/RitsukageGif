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
                if (code.Format is not ("QR_CODE" or "MICRO_QR_CODE" or "RMQR_CODE")) continue;
                var hasOutline = code.Outline.Count >= 3;
                var points = hasOutline ? code.Outline : code.Points;
                if (points.Count < 3 || points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
                    continue;
                var left = points[0].X;
                var top = points[0].Y;
                var right = left;
                var bottom = top;
                if (hasOutline)
                {
                    for (var index = 1; index < points.Count; index++)
                    {
                        var point = points[index];
                        left = Math.Min(left, point.X);
                        top = Math.Min(top, point.Y);
                        right = Math.Max(right, point.X);
                        bottom = Math.Max(bottom, point.Y);
                    }
                }
                else
                {
                    var second = points[1];
                    var third = points[2];
                    var fourth = new BarcodeRecognitionPoint(left + third.X - second.X, top + third.Y - second.Y);
                    left = Math.Min(Math.Min(left, second.X), Math.Min(third.X, fourth.X));
                    top = Math.Min(Math.Min(top, second.Y), Math.Min(third.Y, fourth.Y));
                    right = Math.Max(Math.Max(right, second.X), Math.Max(third.X, fourth.X));
                    bottom = Math.Max(Math.Max(bottom, second.Y), Math.Max(third.Y, fourth.Y));
                }

                var padding = hasOutline ? 0 : Math.Max(4, Math.Max(right - left, bottom - top) / 4);
                var x = (int)Math.Clamp(Math.Floor(left - padding), 0, frame.Width);
                var y = (int)Math.Clamp(Math.Floor(top - padding), 0, frame.Height);
                var x2 = (int)Math.Clamp(hasOutline ? Math.Floor(right) + 1 : Math.Ceiling(right + padding), 0,
                    frame.Width);
                var y2 = (int)Math.Clamp(hasOutline ? Math.Floor(bottom) + 1 : Math.Ceiling(bottom + padding), 0,
                    frame.Height);
                if (x2 > x && y2 > y)
                    result.Add(new(Id, PrivacyCandidateKind.QrCode, new(x, y, x2 - x, y2 - y), code.Text, true));
            }

            return result.AsReadOnly();
        }
    }
}
