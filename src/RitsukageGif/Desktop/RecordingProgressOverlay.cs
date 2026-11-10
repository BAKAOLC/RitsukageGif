using System.Globalization;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class RecordingProgressOverlay
    {
        // ReSharper disable once UnusedMember.Global
        internal static PreviewFrame Render(PreviewFrame source, long frameIndex, RecordingReviewFrameRange range,
            double speed, int originalWidth, RecordingProgressEffects effects, CancellationToken token = default)
        {
            using var buffer = new RecordingOverlayBuffer(source);
            RenderInto(buffer, frameIndex, range, speed, originalWidth, effects, token);
            return buffer.TakeResult();
        }

        internal static void RenderInto(RecordingOverlayBuffer buffer, long frameIndex, RecordingReviewFrameRange range,
            double speed, int originalWidth, RecordingProgressEffects effects, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!effects.Enabled) return;
            var source = buffer.Current;
            var elapsed = Math.Clamp(frameIndex, range.Start, range.End) - range.Start;
            var length = range.End - range.Start;
            var progress = length > 0 ? elapsed / (double)length : 0;
            var scale = source.Width / (float)originalWidth;
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new(0, 0, source.Width, source.Height));
            SKRect affected;
            using var paint = new SKPaint();
            paint.IsAntialias = true;
            if (effects.Bar)
            {
                var height = Math.Max(1, effects.BarHeight * scale);
                affected = new(0, source.Height - height, source.Width, source.Height);
                paint.Color = new(effects.BarBackground);
                canvas.DrawRect(0, source.Height - height, source.Width, height, paint);
                paint.Color = new(effects.BarForeground);
                canvas.DrawRect(0, source.Height - height, (float)(source.Width * progress), height, paint);
            }
            else
            {
                var text = Format(elapsed / (range.FramesPerSecond * speed)) + "/" +
                           Format(length / (range.FramesPerSecond * speed));
                using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
                using var font = new SKFont(typeface, Math.Max(1, effects.FontSize * scale));
                var width = font.MeasureText(text);
                var metrics = font.Metrics;
                var height = metrics.Descent - metrics.Ascent;
                var rectangle = new SKRect(source.Width - width - 16 * scale, source.Height - height - 8 * scale,
                    source.Width, source.Height);
                affected = rectangle;
                paint.Color = new(effects.TextBackground);
                canvas.DrawRoundRect(rectangle, 4 * scale, 4 * scale, paint);
                paint.Color = new(effects.TextForeground);
                canvas.DrawText(text, rectangle.Left + 8 * scale, rectangle.Top + 4 * scale - metrics.Ascent,
                    font, paint);
            }

            using var picture = recorder.EndRecording();
            RecordingOverlayComposition.CompositeInto(buffer, picture, affected, token);
        }

        private static string Format(double seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1
                ? ((int)time.TotalHours).ToString("00", CultureInfo.InvariantCulture) + time.ToString(@"\:mm\:ss")
                : time.ToString(@"mm\:ss");
        }
    }
}
