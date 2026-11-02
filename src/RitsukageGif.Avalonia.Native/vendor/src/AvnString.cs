using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Avalonia.Native.Interop
{
    internal partial interface IAvnString
    {
        string? String { get; }
        byte[] Bytes { get; }
    }

    internal partial interface IAvnStringArray
    {
        string[] ToStringArray();
    }

    internal sealed class AvnString : NativeCallbackBase, IAvnString
    {
        private IntPtr _native;
        private int _nativeLen;

        // ReSharper disable once ConvertToPrimaryConstructor
        public AvnString(string s)
        {
            String = s;
        }

        public string String { get; }
        public byte[] Bytes => Encoding.UTF8.GetBytes(String);

        public unsafe void* Pointer()
        {
            EnsureNative();
            return _native.ToPointer();
        }

        public int Length()
        {
            EnsureNative();
            return _nativeLen;
        }

        protected override void Destroyed()
        {
            if (_native != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_native);
                _native = IntPtr.Zero;
            }
        }

        private unsafe void EnsureNative()
        {
            if (string.IsNullOrEmpty(String))
                return;
            if (_native == IntPtr.Zero)
            {
                _nativeLen = Encoding.UTF8.GetByteCount(String);
                _native = Marshal.AllocHGlobal(_nativeLen + 1);
                var ptr = (byte*)_native.ToPointer();
                fixed (char* chars = String)
                    Encoding.UTF8.GetBytes(chars, String.Length, ptr, _nativeLen);
                ptr[_nativeLen] = 0;
            }
        }
    }

    internal sealed class AvnStringArray : NativeCallbackBase, IAvnStringArray
    {
        private readonly IAvnString[] _items;

        public AvnStringArray(IEnumerable<string> items)
        {
            _items = [.. items.Select(s => s.ToAvnString())];
        }

        public string[] ToStringArray()
        {
            return [.. _items.Select(n => n.String ?? string.Empty)];
        }

        public uint Count => (uint)_items.Length;

        public IAvnString Get(uint index)
        {
            return _items[(int)index];
        }

        protected override void Destroyed()
        {
            foreach (var item in _items) item.Dispose();
        }
    }
}

namespace Avalonia.Native.Interop.Impl
{
    // ReSharper disable once InconsistentNaming
    // ReSharper disable once UnusedType.Global
    internal unsafe partial class __MicroComIAvnStringProxy
    {

        // ReSharper disable once ReplaceWithFieldKeyword
        private string? _managed;
        // ReSharper disable once ReplaceWithFieldKeyword
        private byte[]? _bytes;

        public string? String
        {
            get
            {
                if (_managed == null)
                {
                    var ptr = Pointer();
                    if (ptr == null)
                        return null;
                    _managed = Encoding.UTF8.GetString((byte*)ptr, Length());
                }

                return _managed;
            }
        }

        public byte[] Bytes
        {
            get
            {
                if (_bytes == null)
                {
                    _bytes = new byte[Length()];
                    Marshal.Copy(new(Pointer()), _bytes, 0, _bytes.Length);
                }

                return _bytes;
            }
        }

        public override string? ToString()
        {
            return String;
        }
    }

    // ReSharper disable once InconsistentNaming
    // ReSharper disable once UnusedType.Global
    internal partial class __MicroComIAvnStringArrayProxy
    {
        public string[] ToStringArray()
        {
            var arr = new string[Count];
            for (uint c = 0; c < arr.Length; c++)
                using (var s = Get(c))
                    arr[c] = s.String ?? string.Empty;
            return arr;
        }
    }
}
