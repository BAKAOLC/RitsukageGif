using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedWindowShade : Control, IPinnedInputCoverageSource
    {
        private BoxShadows _shadow;
        private Size _sourceLogicalSize;
        private CaptureWindowShape? _windowShape;

        internal BoxShadows Shadow
        {
            set
            {
                if (_shadow == value) return;
                _shadow = value;
                InvalidateVisual();
                InputCoverageChanged?.Invoke();
            }
        }

        internal Size SourceLogicalSize
        {
            set
            {
                if (_sourceLogicalSize == value) return;
                _sourceLogicalSize = value;
                InvalidateVisual();
                InputCoverageChanged?.Invoke();
            }
        }

        internal CaptureWindowShape? WindowShape
        {
            get => _windowShape;
            set
            {
                if (_windowShape == value) return;
                _windowShape = value;
                InvalidateVisual();
                InputCoverageChanged?.Invoke();
            }
        }

        public event Action? InputCoverageChanged;

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            var radius = _windowShape is not { } shape || shape.IsEmpty ||
                         _sourceLogicalSize.Width <= 0 || _sourceLogicalSize.Height <= 0
                ? default
                : new Vector(Math.Min(.5, shape.RadiusX / _sourceLogicalSize.Width) * bounds.Width,
                    Math.Min(.5, shape.RadiusY / _sourceLogicalSize.Height) * bounds.Height);
            context.DrawRectangle(Brushes.Transparent, null, new(bounds, radius), _shadow);
        }
    }
}
