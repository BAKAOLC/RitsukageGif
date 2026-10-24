using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorCanvas
    {
        private const long HistoryBudget = 64L * 1024 * 1024;
        private readonly List<AnimationEditorDraftState> _undo = [];
        private readonly List<AnimationEditorDraftState> _redo = [];
        private readonly HashSet<Bitmap> _ownedImages = [];
        public bool HasRasterChanges { get; private set; }
        public bool CanUndoDraft => _undo.Count > 0;
        public bool CanRedoDraft => _redo.Count > 0;

        private static AnimationEditorLayer CloneLayer(AnimationEditorLayer layer) => new()
        {
            Text = layer.Text, FontSize = layer.FontSize, Formatted = layer.Formatted,
            FontFamily = layer.FontFamily, Bold = layer.Bold, Italic = layer.Italic, Color = layer.Color,
            Image = layer.Image, Bounds = layer.Bounds, Opacity = layer.Opacity
        };

        private AnimationEditorDraftState Snapshot() => new()
        {
            Pixels = _pixels!.ToArray(), Layers = _layers.Select(CloneLayer).ToList(),
            Changed = _changed, RasterChanged = HasRasterChanges
        };

        private void Remember()
        {
            if (_pixels is null) return;
            _redo.Clear();
            if (_pixels.LongLength > HistoryBudget)
            {
                _undo.Clear();
                PruneImages();
                return;
            }

            _undo.Add(Snapshot());
            while (_undo.Count > 16 || _undo.Count * (long)_pixels.Length > HistoryBudget) _undo.RemoveAt(0);
            PruneImages();
        }

        private void Restore(AnimationEditorDraftState state)
        {
            _pixels = state.Pixels;
            _layers.Clear();
            _layers.AddRange(state.Layers);
            _selected = null;
            _changed = state.Changed;
            HasRasterChanges = state.RasterChanged;
            Upload();
            DraftChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UndoDraft()
        {
            if (_dragging)
            {
                CancelGesture();
                return;
            }

            if (_undo.Count == 0) return;
            _redo.Add(Snapshot());
            var state = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            Restore(state);
        }

        public void RedoDraft()
        {
            if (_dragging)
            {
                CancelGesture();
                return;
            }

            if (_redo.Count == 0) return;
            _undo.Add(Snapshot());
            var state = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            Restore(state);
        }

        private void PruneImages()
        {
            var retained = _layers.Concat(_undo.SelectMany(state => state.Layers))
                .Concat(_redo.SelectMany(state => state.Layers))
                .Where(layer => layer.Image is not null).Select(layer => layer.Image!).ToHashSet();
            foreach (var image in _ownedImages.Where(image => !retained.Contains(image)).ToArray())
            {
                _ownedImages.Remove(image);
                image.Dispose();
            }
        }

        private void ClearHistory()
        {
            _undo.Clear();
            _redo.Clear();
            foreach (var image in _ownedImages) image.Dispose();
            _ownedImages.Clear();
            HasRasterChanges = false;
        }
    }
}
