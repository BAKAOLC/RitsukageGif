using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RitsukageGif.Desktop
{
    [SupportedOSPlatform("windows")]
    internal static unsafe partial class WindowsShellDispatch
    {
        internal static void Release(nint instance)
        {
            if (instance != 0) _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance);
        }

        internal static nint Query(nint instance, Guid interfaceId)
        {
            nint result = 0;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)instance)[0])
                (instance, &interfaceId, &result));
            return result;
        }

        internal static long Integer(nint instance, string name)
        {
            var value = Invoke(instance, name, 2);
            try
            {
                return value.Type switch
                {
                    3 => value.Integer,
                    19 => unchecked((uint)value.Integer),
                    20 => value.Long,
                    _ => throw new InvalidDataException("The Shell returned an invalid numeric property."),
                };
            }
            finally
            {
                _ = VariantClear(ref value);
            }
        }

        internal static string Text(nint instance, string name)
        {
            var value = Invoke(instance, name, 2);
            try
            {
                return value.Type == 8
                    ? Marshal.PtrToStringBSTR(value.Pointer)
                    : throw new InvalidDataException("The Shell returned an invalid text property.");
            }
            finally
            {
                _ = VariantClear(ref value);
            }
        }

        internal static nint Dispatch(nint instance, string name, ushort flags = 2, int? index = null)
        {
            var value = Invoke(instance, name, flags, index);
            try
            {
                if (value.Type is 0 or 1) return 0;
                if (value.Type != 9 || value.Pointer == 0)
                    throw new InvalidDataException("The Shell returned an invalid automation object.");
                var result = value.Pointer;
                value = default;
                return result;
            }
            finally
            {
                _ = VariantClear(ref value);
            }
        }

        private static ShellAutomationVariant Invoke(nint instance, string name, ushort flags, int? index = null)
        {
            var iid = Guid.Empty;
            var member = 0;
            fixed (char* characters = name)
            {
                var address = (nint)characters;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, uint, uint, int*, int>)
                    (*(nint**)instance)[5])(instance, &iid, &address, 1, 0, &member));
            }

            var argument = new ShellAutomationVariant { Type = 3, Integer = index.GetValueOrDefault() };
            var parameters = new ShellDispatchParameters
            {
                Arguments = index.HasValue ? (nint)(&argument) : 0,
                ArgumentCount = index.HasValue ? 1u : 0,
            };
            var result = default(ShellAutomationVariant);
            var exception = default(ShellDispatchException);
            var status = ((delegate* unmanaged[Stdcall]<nint, int, Guid*, uint, ushort, ShellDispatchParameters*,
                    ShellAutomationVariant*, ShellDispatchException*, uint*, int>)(*(nint**)instance)[6])
                (instance, member, &iid, 0, flags, &parameters, &result, &exception, null);
            if (exception.DeferredFill != 0)
                _ = ((delegate* unmanaged[Stdcall]<ShellDispatchException*, int>)exception.DeferredFill)(&exception);
            SysFreeString(exception.Source);
            SysFreeString(exception.Description);
            SysFreeString(exception.HelpFile);
            if (status < 0)
            {
                _ = VariantClear(ref result);
                Marshal.ThrowExceptionForHR(status);
            }

            return result;
        }

        [LibraryImport("oleaut32.dll")]
        private static partial int VariantClear(ref ShellAutomationVariant value);

        [LibraryImport("oleaut32.dll")]
        private static partial void SysFreeString(nint value);
    }
}
