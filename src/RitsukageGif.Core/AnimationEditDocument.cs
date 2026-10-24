using ImageMagick;

namespace RitsukageGif.Core
{
    // Callers serialize access; snapshots own immutable PNG files in this document's private directory.
    public sealed partial class AnimationEditDocument : IDisposable
    {
        private const int HistoryLimit = 32;

        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "RitsukageGif-edit-" + Guid.NewGuid().ToString("N"));

        private readonly List<AnimationEditSnapshot> _undo = [];
        private readonly List<AnimationEditSnapshot> _redo = [];
        private AnimationEditSnapshot _current = new([], 0);
        private bool _disposed;

        private AnimationEditDocument() => Directory.CreateDirectory(_directory);
        public IReadOnlyList<AnimationEditFrame> Frames => _current.Frames;
        public int Width => Frames[0].Width;
        public int Height => Frames[0].Height;
        public uint Iterations => _current.Iterations;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public static AnimationEditDocument Create(int width, int height, int delayMs = 100)
        {
            ValidateSize(width, height);
            ValidateDelay(delayMs);
            var document = new AnimationEditDocument();
            try
            {
                using var image = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
                document._current = new(Array.AsReadOnly(new[] { document.Store(image, delayMs, default) }), 0);
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }

        public static AnimationEditDocument Open(string path, CancellationToken token = default)
        {
            var document = new AnimationEditDocument();
            try
            {
                var frames = document.Read(path, false, token, out var iterations);
                token.ThrowIfCancellationRequested();
                document._current = new(frames.AsReadOnly(), iterations);
                return document;
            }
            catch (Exception error) when (token.IsCancellationRequested &&
                                          error is MagickException or ObjectDisposedException)
            {
                document.Dispose();
                throw new OperationCanceledException(token);
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }

        public void SetDelay(IEnumerable<int> indices, int delayMs)
        {
            ValidateDelay(delayMs);
            var frames = Frames.ToArray();
            foreach (var index in Select(indices)) frames[index] = frames[index] with { DelayMs = delayMs };
            Commit(frames);
        }

        public void SetIterations(uint iterations)
        {
            if (iterations > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(iterations));
            Commit(Frames, iterations);
        }

        public void Duplicate(IEnumerable<int> indices)
        {
            var selected = Select(indices).ToHashSet();
            var frames = new List<AnimationEditFrame>();
            for (var index = 0; index < Frames.Count; index++)
            {
                frames.Add(Frames[index]);
                if (selected.Contains(index)) frames.Add(Frames[index]);
            }

            Commit(frames);
        }

        public void Delete(IEnumerable<int> indices)
        {
            var selected = Select(indices).ToHashSet();
            if (selected.Count == Frames.Count) throw new InvalidOperationException(UiText.Get("EditorKeepFrame"));
            Commit(Frames.Where((_, index) => !selected.Contains(index)).ToArray());
        }

        public void Move(int from, int to)
        {
            Select([from, to]);
            var frames = Frames.ToList();
            var frame = frames[from];
            frames.RemoveAt(from);
            frames.Insert(to, frame);
            Commit(frames);
        }

        public int[] MoveSelected(IReadOnlyCollection<int> indices, int direction)
        {
            if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
            var selected = Select(indices);
            // Treat all selected blocks as one move: a boundary stops the complete selection.
            if (direction < 0 && selected[0] == 0 || direction > 0 && selected[^1] == Frames.Count - 1)
                return selected;
            var frames = Frames.ToArray();
            if (direction < 0)
            {
                foreach (var index in selected)
                    (frames[index - 1], frames[index]) = (frames[index], frames[index - 1]);
            }
            else
            {
                for (var position = selected.Length - 1; position >= 0; position--)
                {
                    var index = selected[position];
                    (frames[index], frames[index + 1]) = (frames[index + 1], frames[index]);
                }
            }

            Commit(frames);
            for (var position = 0; position < selected.Length; position++) selected[position] += direction;
            return selected;
        }

        public void AddBlank(int insertIndex, int delayMs, string color = "transparent")
        {
            CheckInsert(insertIndex);
            ValidateDelay(delayMs);
            using var image = new MagickImage(new MagickColor(color), (uint)Width, (uint)Height);
            var frames = Frames.ToList();
            frames.Insert(insertIndex, Store(image, delayMs, default));
            Commit(frames);
        }

        public void Import(string path, int insertIndex, CancellationToken token = default)
        {
            CheckInsert(insertIndex);
            try
            {
                var added = Read(path, true, token, out _);
                var frames = Frames.ToList();
                frames.InsertRange(insertIndex, added);
                token.ThrowIfCancellationRequested();
                Commit(frames);
            }
            catch (Exception error) when (token.IsCancellationRequested &&
                                          error is MagickException or ObjectDisposedException)
            {
                throw new OperationCanceledException(token);
            }
            finally
            {
                Prune();
            }
        }

        public void Undo()
        {
            ThrowIfDisposed();
            if (!CanUndo) return;
            _redo.Add(_current);
            _current = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
        }

        public void Redo()
        {
            ThrowIfDisposed();
            if (!CanRedo) return;
            _undo.Add(_current);
            _current = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
        }

        private void Commit(IEnumerable<AnimationEditFrame> frames, uint? iterations = null)
        {
            ThrowIfDisposed();
            var snapshot = new AnimationEditSnapshot(Array.AsReadOnly(frames.ToArray()), iterations ?? Iterations);
            _undo.Add(_current);
            if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
            _redo.Clear();
            _current = snapshot;
            Prune();
        }

        private int[] Select(IEnumerable<int> indices)
        {
            ThrowIfDisposed();
            var result = indices.Distinct().Order().ToArray();
            if (result.Length == 0 || result[0] < 0 || result[^1] >= Frames.Count)
                throw new ArgumentOutOfRangeException(nameof(indices));
            return result;
        }

        private void CheckInsert(int index)
        {
            ThrowIfDisposed();
            if (index < 0 || index > Frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void ValidateSize(int width, int height)
        {
            if (width < 1 || height < 1 || width > 65535 || height > 65535 || (long)width * height > 64 * 1024 * 1024)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"));
        }

        private static void ValidateDelay(int delayMs)
        {
            if (delayMs is < 1 or > 86400000) throw new ArgumentOutOfRangeException(nameof(delayMs));
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        private void Prune()
        {
            var used = _undo.Concat(_redo).Append(_current).SelectMany(snapshot => snapshot.Frames)
                .Select(frame => frame.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(_directory))
                if (!used.Contains(path))
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _undo.Clear();
            _redo.Clear();
            _current = new([], 0);
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
