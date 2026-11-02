using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Avalonia.Native
{
    /// <summary>
    ///     Implementation of <see cref="IDataTransfer" /> for Avalonia.Native.
    /// </summary>
    /// <param name="session">
    ///     The clipboard session.
    ///     The <see cref="ClipboardDataTransfer" /> assumes ownership over this instance.
    /// </param>
    internal sealed class ClipboardDataTransfer(ClipboardReadSession session)
        : PlatformDataTransfer
    {
        // ReSharper disable once ReplaceWithPrimaryConstructorParameter
        private readonly ClipboardReadSession _session = session;

        protected override DataFormat[] ProvideFormats()
        {
            using var formats = _session.GetFormats();
            return ClipboardDataFormatHelper.ToDataFormats(formats, _session.IsTextFormat);
        }

        protected override PlatformDataTransferItem[] ProvideItems()
        {
            var itemCount = _session.GetItemCount();
            if (itemCount == 0)
                return [];

            var items = new PlatformDataTransferItem[itemCount];

            for (var i = 0; i < itemCount; ++i)
                items[i] = new ClipboardDataTransferItem(_session, i);

            return items;
        }

        public override void Dispose()
        {
            _session.Dispose();
        }
    }
}
