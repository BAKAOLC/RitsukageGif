using System;
using System.Runtime.InteropServices;
using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    /// <summary>
    ///     Represents a single "session" inside a clipboard, defined by its current change count.
    ///     When the clipboard changes, this session becomes invalid and throws <see cref="ObjectDisposedException" />.
    /// </summary>
    internal sealed class ClipboardReadSession(IAvnClipboard native, long changeCount, bool ownsNative) : IDisposable
    {
        private const int CorEObjectdisposed = unchecked((int)0x80131622);

        private IAvnClipboard? _native = native;

        // ReSharper disable once ReplaceWithPrimaryConstructorParameter
        private readonly long _changeCount = changeCount;

        // ReSharper disable once ReplaceWithPrimaryConstructorParameter
        private readonly bool _ownsNative = ownsNative;

        private IAvnClipboard Native
            => _native ?? throw new ObjectDisposedException(nameof(ClipboardReadSession));

        public void Dispose()
        {
            if (_ownsNative)
                _native?.Dispose();

            _native = null;
        }

        public IAvnStringArray? GetFormats()
        {
            try
            {
                return Native.GetFormats(_changeCount);
            }
            catch (COMException ex) when (IsComObjectDisposedException(ex))
            {
                return null;
            }
        }

        public int GetItemCount()
        {
            try
            {
                return Native.GetItemCount(_changeCount);
            }
            catch (COMException ex) when (IsComObjectDisposedException(ex))
            {
                return 0;
            }
        }

        public IAvnStringArray? GetItemFormats(int index)
        {
            try
            {
                return Native.GetItemFormats(index, _changeCount);
            }
            catch (COMException ex) when (IsComObjectDisposedException(ex))
            {
                return null;
            }
        }

        public IAvnString? GetItemValueAsString(int index, string format)
        {
            try
            {
                return Native.GetItemValueAsString(index, _changeCount, format);
            }
            catch (COMException ex) when (IsComObjectDisposedException(ex))
            {
                return null;
            }
        }

        public IAvnString? GetItemValueAsBytes(int index, string format)
        {
            try
            {
                return Native.GetItemValueAsBytes(index, _changeCount, format);
            }
            catch (COMException ex) when (IsComObjectDisposedException(ex))
            {
                return null;
            }
        }

        public bool IsTextFormat(string format)
        {
            try
            {
                return Native.IsTextFormat(format) != 0;
            }
            catch (COMException)
            {
                return false;
            }
        }

        private static bool IsComObjectDisposedException(COMException exception)
            // The native side returns CorEObjectdisposed if the clipboard has changed (_changeCount doesn't match).
        {
            return exception.HResult == CorEObjectdisposed;
        }
    }
}
