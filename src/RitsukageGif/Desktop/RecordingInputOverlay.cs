using System.Runtime.InteropServices;
using Avalonia.Input;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class RecordingInputOverlay
    {
        // ReSharper disable once UnusedMember.Global
        internal static PreviewFrame Render(PreviewFrame source, TimeSpan originalPosition,
            RecordingInputTraceDocument trace, RecordingInputEffects effects, PixelRect? sourceCrop = null,
            int framesPerSecond = 24, CancellationToken token = default)
        {
            using var buffer = new RecordingOverlayBuffer(source);
            RenderInto(buffer, originalPosition, trace, effects, sourceCrop, framesPerSecond, token);
            return buffer.TakeResult();
        }

        internal static void RenderInto(RecordingOverlayBuffer buffer, TimeSpan originalPosition,
            RecordingInputTraceDocument trace, RecordingInputEffects effects, PixelRect? sourceCrop = null,
            int framesPerSecond = 24, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var source = buffer.Current;
            var frame = trace.FrameAt(originalPosition);
            if (frame is null) return;
            var crop = sourceCrop ?? new PixelRect(0, 0, frame.Width, frame.Height);
            if (crop.IsEmpty) return;
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new(0, 0, source.Width, source.Height));
            var affected = SKRect.Empty;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)source.Width / crop.Width, (float)source.Height / crop.Height);
            canvas.Translate(-crop.X, -crop.Y);
            var next = effects.SmoothTrack ? trace.FrameAfter(originalPosition) : null;
            var duration = TimeSpan.FromSeconds(Math.Max(effects.KeyboardKeepSeconds, 2 / effects.AnimationSpeed));
            var history = new List<RecordingInputEvent>();
            var actions = new List<(TimeSpan Position, RecordingInputEvent Input)>();
            var animatedButtons = 0;
            foreach (var (position, input) in trace.EventsBefore(originalPosition, duration))
            {
                token.ThrowIfCancellationRequested();
                if (trace.HasMouse && frame.MouseStartTimestamp >= 0 && input.Timestamp >= frame.MouseStartTimestamp &&
                    input.Kind is RecordingInputEventKind.PointerDown or RecordingInputEventKind.PointerUp
                        or RecordingInputEventKind.Wheel)
                {
                    if (effects.SimplifyMouseAnimation && input.Kind == RecordingInputEventKind.PointerDown)
                    {
                        if ((animatedButtons & input.Button) != 0) continue;
                        animatedButtons |= input.Button;
                    }

                    actions.Add((position, input));
                }

                if (trace.HasKeyboard && effects.KeyboardCategories != RecordingKeyboardCategories.None &&
                    frame.KeyboardStartTimestamp >= 0 &&
                    input.Timestamp >= frame.KeyboardStartTimestamp &&
                    input.Kind == RecordingInputEventKind.KeyDown &&
                    originalPosition - position <= TimeSpan.FromSeconds(effects.KeyboardKeepSeconds) &&
                    Matches(input, effects.KeyboardCategories))
                    history.Add(input);
            }

            var mouseEffectsEnabled = trace.HasMouse && frame.MouseStartTimestamp >= 0 && effects.ShowCursor;
            var cursorSize = effects.CursorSize *
                             ConventionalScale(frame, originalPosition, actions, effects, framesPerSecond);
            for (var index = actions.Count - 1; index >= 0; index--)
            {
                var (position, input) = actions[index];
                if (mouseEffectsEnabled && input.Kind == RecordingInputEventKind.PointerDown &&
                    (effects.MouseButtons & input.Button) != 0 && trace.FrameAt(position) is { } actionFrame)
                    DrawMouseAction(canvas, actionFrame, input,
                        (originalPosition - position).TotalSeconds * effects.AnimationSpeed, effects, ref affected);
            }

            var belowArea = affected;
            var hasXorCursor = trace.HasCursor && effects.ShowCursor &&
                               frame.Cursor is { Visible: true, AndMask: not null };
            using var belowCursor = hasXorCursor ? recorder.EndRecording() : null;
            if (hasXorCursor)
            {
                canvas = recorder.BeginRecording(new(0, 0, source.Width, source.Height));
                canvas.Scale((float)source.Width / crop.Width, (float)source.Height / crop.Height);
                canvas.Translate(-crop.X, -crop.Y);
                affected = SKRect.Empty;
            }

            if (trace.HasCursor && effects.ShowCursor)
                DrawCursor(canvas, frame, next, originalPosition, cursorSize, ref affected);
            for (var index = actions.Count - 1; index >= 0; index--)
            {
                var (position, input) = actions[index];
                if (mouseEffectsEnabled && input.Kind == RecordingInputEventKind.Wheel && effects.ShowWheel &&
                    trace.FrameAt(position) is { } actionFrame)
                    DrawMouseAction(canvas, actionFrame, input,
                        (originalPosition - position).TotalSeconds * effects.AnimationSpeed, effects, ref affected);
            }

            if (history.Count != 0) DrawKeyboard(canvas, crop, history, effects, ref affected);
            canvas.Flush();
            using var picture = recorder.EndRecording();
            if (belowCursor is not null && !belowArea.IsEmpty)
                RecordingOverlayComposition.CompositeInto(buffer, belowCursor, PreviewArea(belowArea, crop, source),
                    token);
            if (hasXorCursor)
                RecordingXorCursorOverlay.ApplyTo(buffer, frame, next, originalPosition, cursorSize, crop, token);
            if (!affected.IsEmpty)
                RecordingOverlayComposition.CompositeInto(buffer, picture, PreviewArea(affected, crop, source), token);
        }

        private static void DrawCursor(SKCanvas canvas, RecordingInputTraceFrame frame,
            RecordingInputTraceFrame? next, TimeSpan position, double size, ref SKRect affected)
        {
            if (!frame.Cursor.Visible || !frame.Cursor.HasVisiblePixels || frame.Cursor.AndMask is not null) return;
            var cursor = frame.Cursor;
            using var bitmap = new SKBitmap(cursor.Width, cursor.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            Marshal.Copy(cursor.Pixels, 0, bitmap.GetPixels(), cursor.Pixels.Length);
            using var paint = new SKPaint();
            paint.IsAntialias = true;
            for (var index = 0; index < frame.Geometry.Count; index++)
            {
                var geometry = frame.Geometry[index];
                if (!geometry.OwnsCursor || !Contains(geometry.Region, cursor.X, cursor.Y)) continue;
                var point = Map(geometry, cursor.X, cursor.Y);
                if (next is not null && next.Position > frame.Position && next.Cursor.Visible &&
                    next.Cursor.ShapeId == cursor.ShapeId && next.Geometry.Count > index &&
                    next.CaptureSequence != frame.CaptureSequence)
                {
                    var following = next.Geometry[index];
                    if (following.OwnsCursor)
                    {
                        var destination = Map(following, next.Cursor.X, next.Cursor.Y);
                        var fraction = (float)Math.Clamp((position - frame.Position).Ticks /
                                                         (double)(next.Position - frame.Position).Ticks, 0, 1);
                        point = new(point.X + (destination.X - point.X) * fraction,
                            point.Y + (destination.Y - point.Y) * fraction);
                    }
                }

                var scale = size / (100d * cursor.PixelScale);
                var sx = geometry.Destination.Width / (double)geometry.Region.Width;
                var sy = geometry.Destination.Height / (double)geometry.Region.Height;
                var bounds = new SKRect((float)(point.X - cursor.HotspotX * scale * sx),
                    (float)(point.Y - cursor.HotspotY * scale * sy),
                    (float)(point.X + (cursor.Width - cursor.HotspotX) * scale * sx),
                    (float)(point.Y + (cursor.Height - cursor.HotspotY) * scale * sy));
                canvas.Save();
                Clip(canvas, geometry);
                canvas.DrawSampledBitmap(bitmap, bounds, paint);
                Include(ref affected, bounds);
                canvas.Restore();
            }
        }

        private static void DrawMouseAction(SKCanvas canvas, RecordingInputTraceFrame frame,
            RecordingInputEvent input, double age, RecordingInputEffects effects, ref SKRect affected)
        {
            foreach (var geometry in frame.Geometry)
            {
                if (!geometry.OwnsCursor || !Contains(geometry.Region, input.X, input.Y)) continue;
                canvas.Save();
                Clip(canvas, geometry);
                var area = RecordingMouseAnimation.Draw(canvas, Map(geometry, input.X, input.Y),
                    (float)geometry.Destination.Width / geometry.Region.Width,
                    (float)geometry.Destination.Height / geometry.Region.Height, input, age, effects);
                if (!area.IsEmpty) Include(ref affected, area);
                canvas.Restore();
            }
        }

        private static double ConventionalScale(RecordingInputTraceFrame frame, TimeSpan position,
            List<(TimeSpan Position, RecordingInputEvent Input)> actions, RecordingInputEffects effects,
            int framesPerSecond)
        {
            if (effects.MouseStyle != RecordingMouseStyle.Conventional) return 1;
            var effectiveFps = Math.Max(1, (int)Math.Floor(framesPerSecond / effects.AnimationSpeed + 0.5));
            var transitionFrames = Math.Max(1, (int)Math.Floor(effectiveFps * 0.2 + 0.5));
            foreach (var action in actions)
            {
                if (action.Input.Kind is not (RecordingInputEventKind.PointerDown
                        or RecordingInputEventKind.PointerUp) ||
                    (action.Input.Button & effects.MouseButtons) == 0) continue;
                var progress =
                    Math.Clamp((position - action.Position).TotalSeconds * framesPerSecond / transitionFrames, 0, 1);
                var eased = (1 - Math.Cos(progress * Math.PI)) / 2;
                return action.Input.Kind == RecordingInputEventKind.PointerDown ? 1 - 0.2 * eased : 0.8 + 0.2 * eased;
            }

            return (frame.Cursor.Buttons & effects.MouseButtons) != 0 ? 0.8 : 1;
        }

        private static void DrawKeyboard(SKCanvas canvas, PixelRect bounds, List<RecordingInputEvent> history,
            RecordingInputEffects effects, ref SKRect affected)
        {
            var entries = new List<(string Text, int Count, bool Plain)>();
            foreach (var input in history)
            {
                var text = Label(input);
                if (text.Length == 0) continue;
                var plain = IsPlainCharacter(input);
                if (entries.Count != 0)
                {
                    var previous = entries[^1];
                    if (previous.Text == text)
                    {
                        entries[^1] = (previous.Text, previous.Count + 1, previous.Plain);
                        continue;
                    }

                    if (plain && previous is { Plain: true, Count: 1 } &&
                        previous.Text.Length < effects.KeyboardMaxCharacters)
                    {
                        entries[^1] = (text + previous.Text, 1, true);
                        continue;
                    }
                }

                if (entries.Count >= effects.KeyboardMaxHistory) break;
                entries.Add((text, 1, plain));
            }

            using var typeface = SKTypeface.FromFamilyName(effects.KeyboardFont);
            using var font = new SKFont(typeface, effects.KeyboardFontSize);
            using var counterFont = new SKFont(typeface, effects.KeyboardFontSize * 0.8f);
            using var foreground = new SKPaint();
            foreground.IsAntialias = true;
            foreground.Color = new(effects.KeyboardTextColor);
            using var background = new SKPaint();
            background.IsAntialias = true;
            background.Color = new(effects.KeyboardBackground);
            var y = bounds.Bottom - 4f;
            foreach (var entry in entries)
            {
                var separator = entry.Text.LastIndexOf('+');
                var prefix = separator > 0 ? entry.Text[..separator] : null;
                var suffix = prefix is null ? entry.Text : " + " + entry.Text[(separator + 1)..];
                var prefixWidth = prefix is null ? 0 : font.MeasureText(prefix) + 8;
                var textWidth = prefixWidth + font.MeasureText(suffix);
                var counter = entry.Count > 1 ? "x" + entry.Count : null;
                var counterWidth = counter is null ? 0 : counterFont.MeasureText(counter) + 4;
                var width = textWidth + 16 + counterWidth;
                var height = font.Metrics.Descent - font.Metrics.Ascent + 16;
                var x = effects.KeyboardPosition switch
                {
                    1 => bounds.X + (bounds.Width - width) / 2,
                    3 => bounds.Right - width - 4,
                    _ => bounds.X + 4,
                };
                var rect = new SKRect(x, y - height, x + width, y);
                Include(ref affected, rect);
                canvas.DrawRoundRect(rect, 4, 4, background);
                var baseline = rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
                var textX = x + 8;
                if (prefix is not null)
                {
                    var badge = new SKRect(textX - 4, rect.Top + 6, textX + prefixWidth - 4, rect.Bottom - 6);
                    canvas.DrawRoundRect(badge, 4, 4, foreground);
                    canvas.DrawText(prefix, textX, baseline, font, background);
                    textX += prefixWidth;
                }

                canvas.DrawText(suffix, textX, baseline, font, foreground);
                if (counter is not null)
                    canvas.DrawText(counter, x + width - counterWidth, baseline, counterFont, foreground);
                y -= height + 4;
                if (y < bounds.Y) break;
            }
        }

        private static bool Matches(RecordingInputEvent input, RecordingKeyboardCategories categories)
        {
            var modifier = input.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
            var character = input.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or
                >= Key.NumPad0 and <= Key.NumPad9;
            var category = modifier ? RecordingKeyboardCategories.Modifiers :
                input.Modifiers != KeyModifiers.None ? RecordingKeyboardCategories.Combinations :
                character ? RecordingKeyboardCategories.Characters : RecordingKeyboardCategories.Other;
            return (categories & category) != 0;
        }

        private static bool IsPlainCharacter(RecordingInputEvent input)
        {
            return input is { Modifiers: KeyModifiers.None, Key: >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 };
        }

        private static string Label(RecordingInputEvent input)
        {
            var key = input.Key switch
            {
                Key.None => string.Empty,
                Key.LeftCtrl or Key.RightCtrl => "Ctrl",
                Key.LeftAlt or Key.RightAlt => "Alt",
                Key.LeftShift or Key.RightShift => "Shift",
                Key.LWin or Key.RWin => OperatingSystem.IsMacOS() ? "Meta" : "Win",
                >= Key.D0 and <= Key.D9 => ((int)input.Key - (int)Key.D0).ToString(),
                Key.Return => "Enter",
                _ => input.Key.ToString(),
            };
            if (input.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or
                Key.RightShift or Key.LWin or Key.RWin) return key;
            var prefixes = new List<string>(4);
            if ((input.Modifiers & KeyModifiers.Control) != 0) prefixes.Add("Ctrl");
            if ((input.Modifiers & KeyModifiers.Alt) != 0) prefixes.Add("Alt");
            if ((input.Modifiers & KeyModifiers.Shift) != 0) prefixes.Add("Shift");
            if ((input.Modifiers & KeyModifiers.Meta) != 0) prefixes.Add(OperatingSystem.IsMacOS() ? "Meta" : "Win");
            prefixes.Add(key);
            return string.Join('+', prefixes);
        }

        internal static bool Contains(PixelRect region, double x, double y)
        {
            return x >= region.X && x < region.Right && y >= region.Y && y < region.Bottom;
        }

        private static SKPoint Map(RecordingInputGeometry geometry, double x, double y)
        {
            return new((float)(geometry.Destination.X + (x - geometry.Region.X) * geometry.Destination.Width /
                    geometry.Region.Width),
                (float)(geometry.Destination.Y + (y - geometry.Region.Y) * geometry.Destination.Height /
                    geometry.Region.Height));
        }

        private static void Clip(SKCanvas canvas, RecordingInputGeometry geometry)
        {
            canvas.ClipRect(new(geometry.Destination.X, geometry.Destination.Y,
                geometry.Destination.Right, geometry.Destination.Bottom));
            if (geometry.ClipPathData is null) return;
            using var path = SKPath.ParseSvgPathData(geometry.ClipPathData);
            path.FillType = geometry.ClipEvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
        }

        private static SKRect PreviewArea(SKRect area, PixelRect crop, PreviewFrame source)
        {
            return new((area.Left - crop.X) * source.Width / crop.Width,
                (area.Top - crop.Y) * source.Height / crop.Height,
                (area.Right - crop.X) * source.Width / crop.Width,
                (area.Bottom - crop.Y) * source.Height / crop.Height);
        }

        private static void Include(ref SKRect affected, SKRect rect)
        {
            affected = affected.IsEmpty ? rect : SKRect.Union(affected, rect);
        }
    }
}
