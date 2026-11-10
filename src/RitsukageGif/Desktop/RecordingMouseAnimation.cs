using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class RecordingMouseAnimation
    {
        private static readonly SKColor[] ConfettiColors =
            [new(255, 214, 71), new(71, 201, 255), new(225, 181, 255), new(255, 71, 71), new(71, 255, 158)];

        private static double Duration(RecordingMouseStyle style)
        {
            return style switch
            {
                RecordingMouseStyle.Ray => 23 / 29.97,
                RecordingMouseStyle.Floral => 2,
                RecordingMouseStyle.Fireworks or RecordingMouseStyle.Stars => 1,
                RecordingMouseStyle.Ripple => 25 / 29.97,
                _ => 0,
            };
        }

        internal static SKRect Draw(SKCanvas canvas, SKPoint point, float scaleX, float scaleY,
            RecordingInputEvent input, double seconds, RecordingInputEffects effects)
        {
            var wheel = input.Kind == RecordingInputEventKind.Wheel;
            var duration = wheel ? 34 / 30d : Duration(effects.MouseStyle);
            if (seconds < 0 || seconds >= duration || (wheel && effects.WheelStyle == RecordingWheelStyle.None))
                return SKRect.Empty;
            canvas.Save();
            canvas.Translate(point);
            canvas.Scale(scaleX, scaleY);
            canvas.ClipRect(new(-32, -32, 32, 32));
            using var paint = new SKPaint();
            paint.IsAntialias = true;
            paint.Color = new(effects.MouseColor);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeJoin = SKStrokeJoin.Round;
            if (wheel) DrawWheel(canvas, input, seconds, paint);
            else
                switch (effects.MouseStyle)
                {
                    case RecordingMouseStyle.Ray: DrawRay(canvas, seconds, paint); break;
                    case RecordingMouseStyle.Floral: DrawConfetti(canvas, seconds, paint); break;
                    case RecordingMouseStyle.Fireworks: DrawFireworks(canvas, seconds, paint); break;
                    case RecordingMouseStyle.Stars: DrawStars(canvas, seconds, paint); break;
                    case RecordingMouseStyle.Ripple: DrawRipple(canvas, seconds, paint); break;
                    case RecordingMouseStyle.None:
                    case RecordingMouseStyle.Conventional:
                    default:
                        break;
                }

            canvas.Restore();
            return new(point.X - 34 * scaleX, point.Y - 34 * scaleY,
                point.X + 34 * scaleX, point.Y + 34 * scaleY);
        }

        private static void DrawRay(SKCanvas canvas, double seconds, SKPaint paint)
        {
            var frame = seconds * 29.97;
            var head = 10 + 19 * Ease(frame / 15.875);
            var tail = 10 + 19 * Ease((frame - 8.623) / (22 - 8.623));
            if (head <= tail) return;
            paint.StrokeWidth = 4.8f;
            for (var ray = 0; ray < 5; ray++)
            {
                var angle = Math.PI + ray * Math.PI / 4;
                canvas.DrawLine((float)(Math.Cos(angle) * tail), (float)(Math.Sin(angle) * tail),
                    (float)(Math.Cos(angle) * head), (float)(Math.Sin(angle) * head), paint);
            }
        }

        private static void DrawRipple(SKCanvas canvas, double seconds, SKPaint paint)
        {
            var color = paint.Color;
            for (var ring = 0; ring < 2; ring++)
            {
                var duration = ring == 0 ? 19.33375 : 25;
                var frame = seconds * 29.97 - ring * 0.834;
                if (frame < 0 || frame > duration) continue;
                var fade = Ease((frame - (ring == 0 ? 9.666 : 10.5)) /
                                (duration - (ring == 0 ? 9.666 : 10.5)));
                paint.Color = color.WithAlpha((byte)Math.Round(color.Alpha * (1 - fade)));
                paint.StrokeWidth = 2.6f;
                canvas.DrawCircle(0, 0, (float)(28.7 * Ease(frame / duration)), paint);
            }
        }

        private static void DrawStars(SKCanvas canvas, double seconds, SKPaint paint)
        {
            var progress = Ease(seconds);
            paint.Style = SKPaintStyle.Fill;
            paint.Color = new SKColor(255, 204, 96).WithAlpha((byte)Math.Round(255 * Math.Sin(Math.PI * progress)));
            using var path = new SKPath();
            for (var star = 0; star < 8; star++)
            {
                var angle = star * Math.PI / 4;
                var distance = (float)(7 + 20 * progress);
                var center = new SKPoint((float)Math.Cos(angle) * distance, (float)Math.Sin(angle) * distance);
                var radius = (float)((star % 2 == 0 ? 6 : 3.5) * Math.Sin(Math.PI * progress));
                path.Reset();
                for (var vertex = 0; vertex < 8; vertex++)
                {
                    var rotation = vertex * Math.PI / 4;
                    var length = vertex % 2 == 0 ? radius : radius * 0.25f;
                    var point = new SKPoint(center.X + (float)Math.Cos(rotation) * length,
                        center.Y + (float)Math.Sin(rotation) * length);
                    if (vertex == 0) path.MoveTo(point);
                    else path.LineTo(point);
                }

                path.Close();
                canvas.DrawPath(path, paint);
            }
        }

        private static void DrawFireworks(SKCanvas canvas, double seconds, SKPaint paint)
        {
            var head = Ease(seconds / 0.66);
            var tail = Ease((seconds - 0.38) / 0.28);
            if (head <= tail) return;
            for (var ray = 0; ray < 28; ray++)
            {
                var angle = ray * Math.PI * 2 / 28;
                var radius = 18 + ray % 3 * 5;
                var first = new SKPoint((float)(Math.Cos(angle) * radius * tail),
                    (float)(Math.Sin(angle) * radius * tail));
                var last = new SKPoint((float)(Math.Cos(angle) * radius * head),
                    (float)(Math.Sin(angle) * radius * head));
                paint.Color = SKColors.Black;
                paint.StrokeWidth = 2.6f;
                canvas.DrawLine(first, last, paint);
                paint.Color = ray % 2 == 0 ? new(255, 214, 0) : new(213, 0, 213);
                paint.StrokeWidth = 1.4f;
                canvas.DrawLine(first, last, paint);
            }
        }

        private static void DrawConfetti(SKCanvas canvas, double seconds, SKPaint paint)
        {
            paint.Style = SKPaintStyle.Fill;
            for (var piece = 0; piece < 42; piece++)
            {
                var delay = piece % 7 * 0.025;
                var age = seconds - delay;
                if (age is < 0 or > 1.5) continue;
                var angle = piece * 2.399963229728653;
                var speed = 15 + piece % 5 * 3;
                var x = (float)(Math.Cos(angle) * speed * age);
                var y = (float)(Math.Sin(angle) * speed * age + age * age * 8);
                paint.Color = ConfettiColors[piece % ConfettiColors.Length]
                    .WithAlpha((byte)Math.Round(255 * Math.Clamp(1 - age / 1.5, 0, 1)));
                canvas.Save();
                canvas.Translate(x, y);
                canvas.RotateDegrees((float)(piece * 37 + age * 180));
                canvas.DrawRect(new(-1.2f, -2.2f, 1.2f, 2.2f), paint);
                canvas.Restore();
            }
        }

        private static void DrawWheel(SKCanvas canvas, RecordingInputEvent input, double seconds, SKPaint paint)
        {
            if (input.NativeCode == 0x020e) canvas.RotateDegrees(input.WheelDelta < 0 ? 270 : 90);
            else if (input.WheelDelta > 0) canvas.RotateDegrees(180);
            var original = paint.Color;
            paint.StrokeWidth = 3.8f;
            using var path = new SKPath();
            for (var arrow = 0; arrow < 3; arrow++)
            {
                var frame = seconds * 30 - (arrow + 2);
                var travel = frame <= 14 ? Ease(frame / 14) : 1 - Ease((frame - 14) / 14);
                var y = (float)((arrow - 1) * 9.35 + travel * 5.12);
                paint.Color =
                    original.WithAlpha((byte)Math.Round(original.Alpha * arrow switch { 0 => 0.5, 1 => 0.8, _ => 1 }));
                path.Reset();
                path.MoveTo(-7.37f, y - 3.45f);
                path.LineTo(0, y + 3.45f);
                path.LineTo(7.37f, y - 3.45f);
                canvas.DrawPath(path, paint);
            }
        }

        private static double Ease(double value)
        {
            value = Math.Clamp(value, 0, 1);
            return value * value * (3 - 2 * value);
        }
    }
}
