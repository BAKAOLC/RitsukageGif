using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture
    {
        private Dictionary<Layer, PixelRect>? _inputAreas;
        private Dictionary<Layer, (PixelRect Destination, string Path)>? _inputPaths;
        private List<RecordingInputGeometry>? _inputScratch;
        private RecordingInputGeometry[] _inputSnapshot = [];

        internal bool TrackInputGeometry { get; init; }

        internal IReadOnlyList<RecordingInputGeometry> GetInputGeometry(double cursorX, double cursorY)
        {
            if (_inputAreas is null) return [];
            var paths = _inputPaths ??= new();
            var scratch = _inputScratch ??= [];
            scratch.Clear();
            foreach (var layer in _layers)
            {
                if (!_inputAreas.TryGetValue(layer, out var area) || area.IsEmpty) continue;
                var state = _frame.Layers.GetValueOrDefault(layer.Definition.Name);
                var placement = state?.Motion?.At(_frameMotionTime) ?? state?.Placement ?? layer.Definition.Placement;
                var destination = new PixelRect((int)Math.Round(placement.X * _options.PixelDensity / _options.Divisor),
                    (int)Math.Round(placement.Y * _options.PixelDensity / _options.Divisor),
                    (int)Math.Round(placement.Width * _options.PixelDensity / _options.Divisor),
                    (int)Math.Round(placement.Height * _options.PixelDensity / _options.Divisor));
                if (destination.IsEmpty) continue;
                if (!paths.TryGetValue(layer, out var cached) || cached.Destination != destination)
                {
                    using var path = new SKPath(layer.Mask);
                    path.Transform(SKMatrix.CreateScale((float)destination.Width / layer.Bitmap.Width,
                        (float)destination.Height / layer.Bitmap.Height));
                    path.Transform(SKMatrix.CreateTranslation(destination.X, destination.Y));
                    cached = (destination, path.ToSvgPathData());
                    paths[layer] = cached;
                }

                var owns = layer.Definition.Source == CaptureSourceKind.Screen ||
                           RecordingPointerOwnership.Owns(layer.Handle, _windows, cursorX, cursorY);
                scratch.Add(new(area, destination, owns, cached.Path,
                    layer.Mask.FillType == SKPathFillType.EvenOdd));
            }

            if (!scratch.SequenceEqual(_inputSnapshot)) _inputSnapshot = [.. scratch];
            return _inputSnapshot;
        }

        private void ResetInputGeometry()
        {
            if (TrackInputGeometry) (_inputAreas ??= new()).Clear();
        }

        private void RecordInputArea(Layer layer, PixelRect area)
        {
            _inputAreas?[layer] = area;
        }
    }
}
