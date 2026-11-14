using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Win32.Win32Com;

namespace Avalonia.Win32
{
    /// <summary>
    ///     Wraps a Win32 <see cref="Win32Com.IDataObject" /> into a <see cref="IDataTransferItem" />.
    /// </summary>
    /// <param name="oleDataObject">The wrapped OLE data object.</param>
    /// <param name="formats">The formats for this item.</param>
    internal sealed class OleDataObjectToDataTransferItemWrapper(IDataObject oleDataObject, DataFormat[] formats)
        : PlatformDataTransferItem
    {
        private readonly IDataObject _oleDataObject = oleDataObject;
        private readonly DataFormat[] _formats = formats;

        protected override DataFormat[] ProvideFormats()
        {
            return _formats;
        }

        protected override object? TryGetRawCore(DataFormat format)
        {
            return _oleDataObject.TryGet(format);
        }
    }
}
