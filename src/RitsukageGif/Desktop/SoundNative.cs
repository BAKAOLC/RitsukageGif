using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MiniAudioEx.Native;

namespace RitsukageGif.Desktop
{
    internal static partial class SoundNative
    {
        [LibraryImport("miniaudioex", EntryPoint = "ma_sound_init_from_file_w",
            StringMarshalling = StringMarshalling.Utf16)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial ma_result LoadWide(nint engine, string path, ma_sound_flags flags, nint group,
            nint fence, nint sound);

        [LibraryImport("miniaudioex", EntryPoint = "ma_sound_init_from_file",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial ma_result LoadUtf8(nint engine, string path, ma_sound_flags flags, nint group,
            nint fence, nint sound);
    }
}
