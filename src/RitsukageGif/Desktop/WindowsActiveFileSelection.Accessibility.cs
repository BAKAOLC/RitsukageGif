using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowsActiveFileSelection
    {
        private static readonly Guid AccessibilityClass = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
        private static readonly Guid AccessibilityInterface = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");
        private static readonly Guid AccessibilitySelectionInterface = new("5ED5202E-B2AC-47A6-B638-4B0BF140D78E");
        private static readonly int[] AccessibilityFilePathProperties = [30045, 30093, 30005];

        private static bool UsesAccessibleFileSelection(string? program)
        {
            var name = Path.GetFileName(program.AsSpan());
            return name.Equals("Q-Dir.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Q-Dir_x64.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("TOTALCMD.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("TOTALCMD64.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("FreeCommander.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("FreeCommanderXE.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static unsafe List<string> ReadAccessibleFiles(nint root, uint processId, string? program,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0));
            var enabled = CoEnableCallCancellation(0) >= 0;
            var thread = GetCurrentThreadId();
            var cancellation = enabled ? token.Register(() => _ = CoCancelCall(thread, 0)) : default;
            nint automation = 0;
            nint walker = 0;
            nint rootElement = 0;
            nint current = 0;
            nint selection = 0;
            nint view = 0;
            try
            {
                var clsid = AccessibilityClass;
                var iid = AccessibilityInterface;
                Marshal.ThrowExceptionForHR(CoCreateInstance(&clsid, 0, 1, &iid, &automation));
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)
                    (*(nint**)automation)[6])(automation, root, &rootElement));
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)
                    (*(nint**)automation)[8])(automation, &current));
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)
                    (*(nint**)automation)[16])(automation, &walker));
                var reachedRoot = false;
                while (current != 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (ReadAccessibilityInteger(current, 30002) != processId)
                        throw new IOException("The invocation file view is no longer available.");
                    if (selection == 0 && ReadAccessibilityInteger(current, 30003) is 50008 or 50028)
                    {
                        selection = ReadAccessibilityPattern(current, 10001, AccessibilitySelectionInterface);
                        if (selection != 0)
                        {
                            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)current)[1])(current);
                            view = current;
                        }
                    }

                    var equal = 0;
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint, int*, int>)
                        (*(nint**)automation)[3])(automation, current, rootElement, &equal));
                    if (equal != 0)
                    {
                        reachedRoot = true;
                        break;
                    }

                    nint parent = 0;
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)
                        (*(nint**)walker)[3])(walker, current, &parent));
                    WindowsShellDispatch.Release(current);
                    current = parent;
                }

                if (!reachedRoot || selection == 0)
                    throw new NotSupportedException("The focused file view does not expose a readable file selection.");
                var totalCommander = IsTotalCommander(program);
                var tabularNames = totalCommander && ReadAccessibilityText(view, 30012) == "TMyListBox";
                var paths = ReadAccessibleSelection(selection, processId, tabularNames, token);
                return ResolveAccessibleItemPaths(paths, automation, walker, rootElement, view, processId,
                    totalCommander, root, token);
            }
            finally
            {
                WindowsShellDispatch.Release(view);
                WindowsShellDispatch.Release(selection);
                WindowsShellDispatch.Release(current);
                WindowsShellDispatch.Release(rootElement);
                WindowsShellDispatch.Release(walker);
                WindowsShellDispatch.Release(automation);
                cancellation.Dispose();
                if (enabled) _ = CoDisableCallCancellation(0);
                CoUninitialize();
            }
        }

        private static unsafe List<string> ReadAccessibleSelection(nint selection, uint processId, bool tabularNames,
            CancellationToken token)
        {
            nint elements = 0;
            try
            {
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)
                    (*(nint**)selection)[3])(selection, &elements));
                if (elements == 0)
                    throw new IOException("The file view returned no selection array.");
                var count = 0;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)
                    (*(nint**)elements)[3])(elements, &count));
                var paths = new List<string>(count);
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    nint item = 0;
                    try
                    {
                        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)
                            (*(nint**)elements)[4])(elements, index, &item));
                        if (ReadAccessibilityInteger(item, 30002) != processId ||
                            ReadAccessibilityInteger(item, 30003) is not (50007 or 50029))
                            throw new NotSupportedException("The selected object is not a file-list item.");
                        var path = ReadAccessibleItemPath(item, tabularNames) ??
                                   ReadAccessibleLeafName(item, tabularNames);
                        if (path is null)
                            throw new NotSupportedException(
                                "The file view does not expose filesystem item names or paths.");
                        paths.Add(path);
                    }
                    finally
                    {
                        WindowsShellDispatch.Release(item);
                    }
                }

                return paths;
            }
            finally
            {
                WindowsShellDispatch.Release(elements);
            }
        }

        private static string? ReadAccessibleItemPath(nint item, bool tabularNames)
        {
            foreach (var property in AccessibilityFilePathProperties)
            {
                var value = ReadAccessibilityProperty(item, property);
                try
                {
                    if (value.Type != 8 || value.Pointer == 0) continue;
                    var path = Marshal.PtrToStringBSTR(value.Pointer);
                    if (tabularNames && property == 30005 && path.IndexOf('\t') is >= 0 and var separator)
                        path = path[..separator];
                    if (Path.IsPathFullyQualified(path) && (File.Exists(path) || Directory.Exists(path))) return path;
                }
                finally
                {
                    _ = ClearAccessibilityVariant(ref value);
                }
            }

            return null;
        }

        private static int ReadAccessibilityInteger(nint element, int property)
        {
            var value = ReadAccessibilityProperty(element, property);
            try
            {
                if (value.Type != 3)
                    throw new IOException("The file view returned an invalid identity property.");
                return value.Integer;
            }
            finally
            {
                _ = ClearAccessibilityVariant(ref value);
            }
        }

        private static unsafe ShellAutomationVariant ReadAccessibilityProperty(nint element, int property)
        {
            var value = default(ShellAutomationVariant);
            var status = ((delegate* unmanaged[Stdcall]<nint, int, ShellAutomationVariant*, int>)
                (*(nint**)element)[10])(element, property, &value);
            if (status < 0)
            {
                _ = ClearAccessibilityVariant(ref value);
                Marshal.ThrowExceptionForHR(status);
            }

            return value;
        }

        private static unsafe nint ReadAccessibilityPattern(nint element, int pattern, Guid iid)
        {
            nint result = 0;
            var status = ((delegate* unmanaged[Stdcall]<nint, int, Guid*, nint*, int>)
                (*(nint**)element)[14])(element, pattern, &iid, &result);
            if (status is unchecked((int)0x80040204) or unchecked((int)0x80004002)) return 0;
            Marshal.ThrowExceptionForHR(status);
            return result;
        }

        [LibraryImport("oleaut32.dll", EntryPoint = "VariantClear")]
        private static partial int ClearAccessibilityVariant(ref ShellAutomationVariant value);
    }
}
