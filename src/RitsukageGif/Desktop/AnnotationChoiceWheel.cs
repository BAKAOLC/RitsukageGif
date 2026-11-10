using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class AnnotationChoiceWheel
    {
        private static long _lastStepAt = Environment.TickCount64 - 40;

        internal static bool TryStep(Vector delta, int index, int count, out int next)
        {
            next = index;
            if (count == 0) return false;
            var now = Environment.TickCount64;
            if (now - _lastStepAt < 40) return false;
            _lastStepAt = now;
            var direction = delta.Y != 0 ? delta.Y : delta.X;
            if (direction == 0) return false;
            next = Math.Clamp(index + (direction > 0 ? -1 : 1), 0, count - 1);
            return next != index;
        }
    }
}
