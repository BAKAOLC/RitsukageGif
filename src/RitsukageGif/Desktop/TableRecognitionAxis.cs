namespace RitsukageGif.Desktop
{
    internal sealed class TableRecognitionAxis
    {
        private bool[] _custom = [];
        private double _defaultSize;
        private double[] _positions = [0];
        private double[] _sizes = [];
        internal int Count => _sizes.Length;
        internal double Total => _positions[^1];

        internal double Position(int index)
        {
            return _positions[index];
        }

        internal double Extent(int index)
        {
            return _sizes[index];
        }

        internal void Reset(int count, double size, bool preserve)
        {
            _defaultSize = size;
            var old = _sizes.Length;
            if (!preserve)
            {
                _sizes = new double[count];
                _custom = new bool[count];
                old = 0;
            }
            else
            {
                Array.Resize(ref _sizes, count);
                Array.Resize(ref _custom, count);
            }

            for (var index = 0; index < count; index++)
                if (index >= old || !_custom[index])
                    _sizes[index] = size;
            Array.Resize(ref _positions, count + 1);
            Rebuild(0);
        }

        internal void Resize(int index, double size)
        {
            if (_sizes[index].Equals(size)) return;
            _sizes[index] = size;
            _custom[index] = true;
            Rebuild(index);
        }

        internal TableRecognitionAxisState Capture()
        {
            return new((double[])_sizes.Clone(), (bool[])_custom.Clone());
        }

        internal void Restore(TableRecognitionAxisState state)
        {
            _sizes = (double[])state.Sizes.Clone();
            _custom = (bool[])state.Custom.Clone();
            for (var index = 0; index < Count; index++)
                if (!_custom[index])
                    _sizes[index] = _defaultSize;
            _positions = new double[_sizes.Length + 1];
            Rebuild(0);
        }

        internal void Insert(int index)
        {
            var sizes = new double[Count + 1];
            var custom = new bool[Count + 1];
            Array.Copy(_sizes, 0, sizes, 0, index);
            Array.Copy(_custom, 0, custom, 0, index);
            sizes[index] = _defaultSize;
            Array.Copy(_sizes, index, sizes, index + 1, Count - index);
            Array.Copy(_custom, index, custom, index + 1, Count - index);
            _sizes = sizes;
            _custom = custom;
            _positions = new double[Count + 1];
            Rebuild(0);
        }

        internal void Remove(IReadOnlyList<int> indices)
        {
            var sizes = new double[Count - indices.Count];
            var custom = new bool[sizes.Length];
            var deleted = 0;
            for (var index = 0; index < Count; index++)
            {
                if (deleted < indices.Count && indices[deleted] == index)
                {
                    deleted++;
                    continue;
                }

                sizes[index - deleted] = _sizes[index];
                custom[index - deleted] = _custom[index];
            }

            _sizes = sizes;
            _custom = custom;
            _positions = new double[Count + 1];
            Rebuild(0);
        }

        internal int Find(double position)
        {
            if (Count == 0) return -1;
            var found = Array.BinarySearch(_positions, position);
            return Math.Clamp(found >= 0 ? found : ~found - 1, 0, Count - 1);
        }

        internal int Boundary(double position, double tolerance)
        {
            if (Count == 0) return -1;
            var index = Find(position);
            if (index > 0 && Math.Abs(position - _positions[index]) <= tolerance) return index - 1;
            return Math.Abs(position - _positions[index + 1]) <= tolerance ? index : -1;
        }

        private void Rebuild(int start)
        {
            for (var index = start; index < _sizes.Length; index++)
                _positions[index + 1] = _positions[index] + _sizes[index];
        }
    }
}
