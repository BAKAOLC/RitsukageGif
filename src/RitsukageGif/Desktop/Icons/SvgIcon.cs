using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace RitsukageGif.Desktop.Icons
{
    internal sealed class SvgIcon : TemplatedControl
    {
        private readonly CachedSvgIcon _icon;
        private readonly Pen?[] _pens;

        static SvgIcon()
        {
            AffectsRender<SvgIcon>(ForegroundProperty);
        }

        internal SvgIcon(IconDefinition definition)
        {
            _icon = IconCatalog.GetCached(definition);
            _pens = new Pen?[_icon.Paths.Length];
            Width = 16;
            Height = 16;
            ClipToBounds = false;
            UpdatePens();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            using var transform = context.PushTransform(_icon.GetTransform(Bounds.Size));
            for (var index = 0; index < _icon.Paths.Length; index++)
            {
                var path = _icon.Paths[index];
                context.DrawGeometry(path.Definition.Fill ? Foreground : null, _pens[index], path.Geometry);
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ForegroundProperty) UpdatePens();
        }

        private void UpdatePens()
        {
            for (var index = 0; index < _icon.Paths.Length; index++)
            {
                var path = _icon.Paths[index];
                if (!path.Definition.Stroke) continue;
                if (_pens[index] is { } pen) pen.Brush = Foreground;
                else _pens[index] = path.CreatePen(Foreground);
            }
        }
    }
}
