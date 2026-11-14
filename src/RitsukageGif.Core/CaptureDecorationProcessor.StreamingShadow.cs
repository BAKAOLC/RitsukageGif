using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    public static partial class CaptureDecorationProcessor
    {
        public sealed partial class StreamingRows
        {
            private delegate void ReadFloatRow(int y, Span<float> row, CancellationToken token);

            private sealed class ShadowRows : IDisposable
            {
                private readonly StreamingRows _owner;
                private readonly float[] _result;
                private readonly RowRing _sourceRows;
                private readonly BoxStage[] _stages;

                internal ShadowRows(StreamingRows owner, int radius)
                {
                    _owner = owner;
                    _result = new float[owner.Geometry.Width];
                    _sourceRows = new(radius + 1, owner._sourceRow.Length);
                    var stages = new List<BoxStage>(3);
                    ReadFloatRow read = ReadMask;
                    try
                    {
                        foreach (var support in new[] { radius / 3, (radius + 1) / 3, (radius + 2) / 3 })
                        {
                            if (support == 0) continue;
                            var stage = new BoxStage(owner.Geometry.Width, owner.Geometry.Height, support, read);
                            stages.Add(stage);
                            read = stage.Read;
                        }

                        _stages = [.. stages];
                    }
                    catch
                    {
                        foreach (var stage in stages) stage.Dispose();
                        _sourceRows.Dispose();
                        throw;
                    }
                }

                public void Dispose()
                {
                    foreach (var stage in _stages) stage.Dispose();
                    _sourceRows.Dispose();
                }

                internal ReadOnlySpan<float> Read(int y, CancellationToken token)
                {
                    _stages[^1].Read(y, _result, token);
                    return _result;
                }

                internal ReadOnlySpan<byte> SourceRow(int y)
                {
                    _sourceRows.Read(y, _owner._sourceRow);
                    return _owner._sourceRow;
                }

                private void ReadMask(int y, Span<float> row, CancellationToken token)
                {
                    row.Clear();
                    var sourceY = y - _owner.Geometry.ContentOffset.Y;
                    if ((uint)sourceY >= (uint)_owner._height) return;
                    var bytes = _owner._sourceRow;
                    _owner._source(sourceY, bytes, token);
                    _sourceRows.Write(sourceY, bytes);
                    var left = _owner.Geometry.ContentOffset.X;
                    for (var x = 0; x < _owner._width; x++)
                    {
                        if ((x & 4095) == 0) token.ThrowIfCancellationRequested();
                        var alpha = _owner.ReadAlpha(bytes.AsSpan(x * _owner._bytesPerPixel, _owner._bytesPerPixel));
                        row[x + left] = alpha * (float)Coverage(x + .5, sourceY + .5, 0, 0,
                            _owner._width, _owner._height, _owner._corner);
                    }
                }
            }

            private sealed class BoxStage : IDisposable
            {
                private readonly double _divisor;
                private readonly int _height, _radius;
                private readonly ReadFloatRow _read;
                private readonly RowRing _rows;
                private readonly float[] _source, _horizontal;
                private readonly double[] _verticalSums;
                private int _current = -1;
                private int _loaded = -1;

                internal BoxStage(int width, int height, int radius, ReadFloatRow read)
                {
                    _height = height;
                    _radius = radius;
                    _read = read;
                    _divisor = 2d * radius + 1;
                    _source = new float[width];
                    _horizontal = new float[width];
                    _verticalSums = new double[width];
                    _rows = new(checked(2 * radius + 1), checked(width * sizeof(float)));
                }

                public void Dispose()
                {
                    _rows.Dispose();
                }

                internal void Read(int y, Span<float> result, CancellationToken token)
                {
                    for (; _current < y; _current++)
                    {
                        token.ThrowIfCancellationRequested();
                        var leaving = _current - _radius;
                        if (leaving >= 0)
                        {
                            _rows.Read(leaving, MemoryMarshal.AsBytes(_horizontal.AsSpan()));
                            Accumulate(-1);
                        }

                        var target = Math.Min(_height - 1, _current + 1 + _radius);
                        while (_loaded < target)
                        {
                            _read(++_loaded, _source, token);
                            var sum = 0d;
                            for (var i = 0; i <= Math.Min(_radius, _source.Length - 1); i++) sum += _source[i];
                            for (var x = 0; x < _source.Length; x++)
                            {
                                _horizontal[x] = (float)Math.Clamp(sum / _divisor, 0, 1);
                                if (x >= _radius) sum -= _source[x - _radius];
                                if (x + _radius + 1 < _source.Length) sum += _source[x + _radius + 1];
                            }

                            _rows.Write(_loaded, MemoryMarshal.AsBytes(_horizontal.AsSpan()));
                            Accumulate(1);
                        }
                    }

                    for (var x = 0; x < result.Length; x++)
                        result[x] = (float)Math.Clamp(_verticalSums[x] / _divisor, 0, 1);
                }

                private void Accumulate(int sign)
                {
                    for (var x = 0; x < _horizontal.Length; x++)
                        _verticalSums[x] += _horizontal[x] * sign;
                }
            }

            private sealed class RowRing : IDisposable
            {
                private const int MaximumMemoryBytes = 8 * 1024 * 1024;
                private readonly FileStream? _file;
                private readonly byte[]? _memory;
                private readonly int _rows, _rowBytes;

                internal RowRing(int rows, int rowBytes)
                {
                    _rows = rows;
                    _rowBytes = rowBytes;
                    var capacity = (long)rows * rowBytes;
                    if (capacity <= MaximumMemoryBytes)
                        _memory = new byte[(int)capacity];
                    else
                        _file = new(Path.Combine(Path.GetTempPath(), $"rg-shadow-{Guid.NewGuid():N}.tmp"),
                            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1,
                            FileOptions.DeleteOnClose | FileOptions.RandomAccess);
                }

                public void Dispose()
                {
                    _file?.Dispose();
                }

                internal void Write(int row, ReadOnlySpan<byte> data)
                {
                    // ReSharper disable once InlineTemporaryVariable
                    if (_memory is { } memory)
                    {
                        data.CopyTo(memory.AsSpan(row % _rows * _rowBytes, _rowBytes));
                    }
                    else
                    {
                        _file!.Position = (long)(row % _rows) * _rowBytes;
                        _file.Write(data);
                    }
                }

                internal void Read(int row, Span<byte> data)
                {
                    // ReSharper disable once InlineTemporaryVariable
                    if (_memory is { } memory)
                    {
                        memory.AsSpan(row % _rows * _rowBytes, _rowBytes).CopyTo(data);
                    }
                    else
                    {
                        _file!.Position = (long)(row % _rows) * _rowBytes;
                        _file.ReadExactly(data);
                    }
                }
            }
        }
    }
}
