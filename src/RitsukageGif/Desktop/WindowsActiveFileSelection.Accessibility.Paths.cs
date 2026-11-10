using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowsActiveFileSelection
    {
        private static List<string> ResolveAccessibleItemPaths(List<string> values, nint automation, nint walker,
            nint root, nint view, uint processId, bool totalCommander, nint rootWindow, CancellationToken token)
        {
            if (values.All(Path.IsPathFullyQualified)) return values;
            if (!IsAccessibleFileView(view))
                throw new NotSupportedException("The focused view does not expose a supported filesystem file list.");
            var folder = (totalCommander
                             ? ReadTotalCommanderFolder(automation, rootWindow, view, processId, token)
                             : null)
                         ?? FindAccessiblePaneFolder(automation, walker, root, view, processId, token);
            for (var index = 0; index < values.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                if (Path.IsPathFullyQualified(values[index])) continue;
                var path = Path.Combine(folder, values[index]);
                if (!File.Exists(path) && !Directory.Exists(path))
                    throw new NotSupportedException("Selected file names do not match the active filesystem folder.");
                values[index] = path;
            }

            return values;
        }

        private static string FindAccessiblePaneFolder(nint automation, nint walker, nint root, nint view,
            uint processId, CancellationToken token)
        {
            var scope = AccessibleParent(walker, view);
            try
            {
                while (scope != 0)
                {
                    token.ThrowIfCancellationRequested();
                    var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    CollectAccessiblePaneFolders(automation, walker, scope, view, processId, directories, token);
                    switch (directories.Count)
                    {
                        case > 1:
                            throw new NotSupportedException(
                                "The active pane has more than one possible filesystem folder.");
                        case 1:
                            return directories.First();
                    }

                    if (AccessibleElementsEqual(automation, scope, root)) break;
                    var parent = AccessibleParent(walker, scope);
                    WindowsShellDispatch.Release(scope);
                    scope = parent;
                }

                throw new NotSupportedException("The active pane does not expose its filesystem folder.");
            }
            finally
            {
                WindowsShellDispatch.Release(scope);
            }
        }

        private static unsafe void CollectAccessiblePaneFolders(nint automation, nint walker, nint element, nint view,
            uint processId, HashSet<string> directories, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ReadAccessibilityInteger(element, 30002) != processId)
                throw new IOException("The active file pane is no longer available.");
            if (AccessibleElementsEqual(automation, element, view)) return;
            if (ReadAccessibilityBoolean(element, 30022)) return;
            var className = ReadAccessibilityText(element, 30012);
            if (IsAccessibleFileViewClass(className))
                throw new NotSupportedException("The folder source cannot be isolated from another file pane.");
            var type = ReadAccessibilityInteger(element, 30003);
            switch (type)
            {
                case 50023:
                case 50004 when ReadAccessibilityBoolean(element, 30019):
                    return;
            }

            CollectAccessibleDirectoryValues(element, directories,
                className == "ComboBoxEx32");
            if (type == 50003) return;
            nint child = 0;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)
                (*(nint**)walker)[4])(walker, element, &child));
            try
            {
                while (child != 0)
                {
                    CollectAccessiblePaneFolders(automation, walker, child, view, processId, directories, token);
                    nint next = 0;
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)
                        (*(nint**)walker)[6])(walker, child, &next));
                    WindowsShellDispatch.Release(child);
                    child = next;
                }
            }
            finally
            {
                WindowsShellDispatch.Release(child);
            }
        }

        private static void CollectAccessibleDirectoryValues(nint element, HashSet<string> directories,
            bool includeName)
        {
            foreach (var property in AccessibilityFilePathProperties)
            {
                if (property == 30005 && !includeName) continue;
                var text = ReadAccessibilityText(element, property);
                if (string.IsNullOrEmpty(text)) continue;
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !uri.IsFile &&
                    (text.Contains("://", StringComparison.Ordinal) ||
                     text.StartsWith("search-ms:", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)))
                {
                    if (includeName)
                        throw new NotSupportedException("The active pane is a virtual or search location.");
                    continue;
                }

                if (!Path.IsPathFullyQualified(text)) continue;
                if (Path.GetFileName(text) is "*" or "*.*")
                    text = Path.GetDirectoryName(text)!;
                if (Directory.Exists(text))
                    directories.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(text)));
                else if (includeName)
                    throw new NotSupportedException("The active pane is not an available filesystem directory.");
            }
        }

        private static bool IsAccessibleFileView(nint element)
        {
            var name = ReadAccessibilityText(element, 30012);
            return IsAccessibleFileViewClass(name);
        }

        private static bool IsAccessibleFileViewClass(string? name)
        {
            return name is "SysListView32" or "ListBox" or "TMyListBox";
        }

        private static string? ReadAccessibleLeafName(nint item, bool tabularNames)
        {
            var name = ReadAccessibilityText(item, 30005);
            if (tabularNames && name?.IndexOf('\t') is { } separator and >= 0)
                name = name[..separator];
            if (name is null or "" or "." or "..") return null;
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var character in name)
                if (character is < ' ' or '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                    return null;
            return name;
        }

        private static string? ReadAccessibilityText(nint element, int property)
        {
            var value = ReadAccessibilityProperty(element, property);
            try
            {
                return value.Type == 8 && value.Pointer != 0 ? Marshal.PtrToStringBSTR(value.Pointer) : null;
            }
            finally
            {
                _ = ClearAccessibilityVariant(ref value);
            }
        }

        private static bool ReadAccessibilityBoolean(nint element, int property)
        {
            var value = ReadAccessibilityProperty(element, property);
            try
            {
                if (value.Type != 11)
                    throw new IOException("The file view returned an invalid visibility property.");
                return value.Integer != 0;
            }
            finally
            {
                _ = ClearAccessibilityVariant(ref value);
            }
        }

        private static unsafe nint AccessibleParent(nint walker, nint element)
        {
            nint parent = 0;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)
                (*(nint**)walker)[3])(walker, element, &parent));
            return parent;
        }

        private static unsafe bool AccessibleElementsEqual(nint automation, nint first, nint second)
        {
            var equal = 0;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint, int*, int>)
                (*(nint**)automation)[3])(automation, first, second, &equal));
            return equal != 0;
        }
    }
}
