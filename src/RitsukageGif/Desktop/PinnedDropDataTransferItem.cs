using System.Collections.ObjectModel;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedDropDataTransferItem(IReadOnlyDictionary<DataFormat, object> values)
        : IAsyncDataTransferItem
    {
        private readonly ReadOnlyDictionary<DataFormat, object> _values =
            new(new Dictionary<DataFormat, object>(values));

        public IReadOnlyList<DataFormat> Formats { get; } = Array.AsReadOnly<DataFormat>([.. values.Keys]);

        public Task<object?> TryGetRawAsync(DataFormat format)
        {
            var value = _values.GetValueOrDefault(format);
            return Task.FromResult(value);
        }
    }
}
