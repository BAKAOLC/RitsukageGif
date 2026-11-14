using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture
    {
        private Dictionary<Layer, PixelRect>? _inputAreas;
        private Dictionary<Layer, (SKRect Destination, int RasterWidth, int RasterHeight, string Path)>? _inputPaths;
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
                if (!_inputAreas.TryGetValue(layer, out var area) || area.IsEmpty || layer.Destination.IsEmpty ||
                    _frame.Layers.GetValueOrDefault(layer.Definition.Name)?.Opacity == 0) continue;
                var destination = layer.Destination;
                var rasterWidth = layer.Bitmap.Width;
                var rasterHeight = layer.Bitmap.Height;
                string? clip = null;
                if (layer.Definition.Shape.Kind != "rect")
                {
                    if (!paths.TryGetValue(layer, out var cached) || cached.Destination != destination ||
                        cached.RasterWidth != rasterWidth || cached.RasterHeight != rasterHeight)
                    {
                        using var path = new SKPath(layer.Mask);
                        path.Transform(SKMatrix.CreateScale(destination.Width / rasterWidth,
                            destination.Height / rasterHeight));
                        path.Transform(SKMatrix.CreateTranslation(destination.Left, destination.Top));
                        cached = (destination, rasterWidth, rasterHeight, path.ToSvgPathData());
                        paths[layer] = cached;
                    }

                    clip = cached.Path;
                }

                var owns = layer.Definition.Source == CaptureSourceKind.Screen ||
                           RecordingPointerOwnership.Owns(layer.Handle, _windows, cursorX, cursorY);
                scratch.Add(new(area, destination, owns, clip,
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
