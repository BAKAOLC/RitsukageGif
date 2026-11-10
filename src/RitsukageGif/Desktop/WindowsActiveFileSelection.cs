using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RitsukageGif.Desktop
{
    [SupportedOSPlatform("windows")]
    internal static partial class WindowsActiveFileSelection
    {
        private static readonly SemaphoreSlim Reader = new(1, 1);
        private static readonly Guid ShellWindowsClass = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid ShellWindowsInterface = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
        private static readonly Guid ServiceProviderInterface = new("6D5140C1-7436-11CE-8034-00AA006009FA");
        private static readonly Guid TopLevelBrowserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid ShellBrowserInterface = new("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid DispatchInterface = new("00020400-0000-0000-C000-000000000046");

        internal static async Task<IReadOnlyList<string>> ReadAsync(CancellationToken token)
        {
            var root = CaptureForeground(out var processId);
            if (root == 0) return [];
            var program = ForegroundHotkeyContext.ReadProgram(root);
            if (program is not null &&
                Path.GetFileName(program).Equals("dopus.exe", StringComparison.OrdinalIgnoreCase))
                return await DirectoryOpusActiveFileSelection.ReadAsync(root, program, token);
            var accessible = UsesAccessibleFileSelection(program);
            await Reader.WaitAsync(token);
            var completion =
                new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    completion.TrySetResult(accessible
                        ? ReadAccessibleFiles(root, processId, program, token)
                        : Read(root, token));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    completion.TrySetCanceled(token);
                }
                catch (COMException) when (token.IsCancellationRequested)
                {
                    completion.TrySetCanceled(token);
                }
                catch (COMException error)
                {
                    completion.TrySetException(new IOException("File selection could not be read.", error));
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
                finally
                {
                    Reader.Release();
                }
            }) { IsBackground = true, Name = "Shell file selection" };
            try
            {
                thread.SetApartmentState(accessible ? ApartmentState.MTA : ApartmentState.STA);
                thread.Start();
            }
            catch
            {
                Reader.Release();
                throw;
            }

            return await completion.Task.WaitAsync(token);
        }

        private static nint CaptureForeground(out uint processId)
        {
            processId = 0;
            var foreground = GetForegroundWindow();
            if (foreground == 0) return 0;
            var thread = GetWindowThreadProcessId(foreground, out processId);
            var information = new WindowsShellThreadInfo { Size = (uint)Marshal.SizeOf<WindowsShellThreadInfo>() };
            if (thread != 0 && GetGuiThreadInfo(thread, ref information) != 0 && information.Focus != 0)
            {
                var owner = GetAncestor(information.Focus, 3);
                if (owner != 0)
                {
                    _ = GetWindowThreadProcessId(owner, out processId);
                    return owner;
                }
            }

            return foreground;
        }

        private static unsafe List<string> Read(nint foreground, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2));
            var cancellationEnabled = CoEnableCallCancellation(0) >= 0;
            var thread = GetCurrentThreadId();
            var cancellation = cancellationEnabled
                ? token.Register(() => _ = CoCancelCall(thread, 0))
                : default;
            nint windows = 0;
            try
            {
                var clsid = ShellWindowsClass;
                var iid = ShellWindowsInterface;
                Marshal.ThrowExceptionForHR(CoCreateInstance(&clsid, 0, 0x17, &iid, &windows));
                var count = checked((int)WindowsShellDispatch.Integer(windows, "Count"));
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    nint browser = 0;
                    try
                    {
                        browser = WindowsShellDispatch.Dispatch(windows, "Item", 3, index);
                        if (browser == 0) continue;
                        var handle = unchecked((uint)WindowsShellDispatch.Integer(browser, "HWND"));
                        if (handle != unchecked((uint)(nuint)foreground)) continue;
                        var selected = ReadView(browser, foreground, token);
                        if (selected is not null) return selected;
                    }
                    catch (COMException error) when (error.HResult is unchecked((int)0x80010108) or
                                                         unchecked((int)0x80004002) or unchecked((int)0x8002000B))
                    {
                    }
                    finally
                    {
                        WindowsShellDispatch.Release(browser);
                    }
                }

                var className = stackalloc char[128];
                var length = GetClassNameW(foreground, className, 128);
                var desktop = new ReadOnlySpan<char>(className, length);
                if (desktop is not ("Progman" or "WorkerW")) return [];
                var location = new ShellAutomationVariant { Type = 3, Integer = 0 };
                var unused = default(ShellAutomationVariant);
                var handle32 = 0;
                nint desktopBrowser = 0;
                var status = ((delegate* unmanaged[Stdcall]<nint, ShellAutomationVariant*, ShellAutomationVariant*,
                        int, int*, int, nint*, int>)(*(nint**)windows)[15])
                    (windows, &location, &unused, 8, &handle32, 1, &desktopBrowser);
                if (status == 1 || desktopBrowser == 0) return [];
                Marshal.ThrowExceptionForHR(status);
                try
                {
                    return ReadView(desktopBrowser, foreground, token, true) ?? [];
                }
                finally
                {
                    WindowsShellDispatch.Release(desktopBrowser);
                }
            }
            catch (COMException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
            finally
            {
                WindowsShellDispatch.Release(windows);
                cancellation.Dispose();
                if (cancellationEnabled) _ = CoDisableCallCancellation(0);
                CoUninitialize();
            }
        }

        private static unsafe List<string>? ReadView(nint browser, nint foreground, CancellationToken token,
            bool desktop = false)
        {
            var provider = WindowsShellDispatch.Query(browser, ServiceProviderInterface);
            nint shellBrowser = 0;
            nint view = 0;
            nint document = 0;
            nint selected = 0;
            try
            {
                var service = TopLevelBrowserService;
                var iid = ShellBrowserInterface;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)
                    (*(nint**)provider)[3])(provider, &service, &iid, &shellBrowser));
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)
                    (*(nint**)shellBrowser)[15])(shellBrowser, &view));
                nint viewWindow = 0;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)
                    (*(nint**)view)[3])(view, &viewWindow));
                if (IsWindowVisible(viewWindow) == 0 || (!desktop && GetAncestor(viewWindow, 3) != foreground))
                    return null;
                iid = DispatchInterface;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)
                    (*(nint**)view)[15])(view, 0, &iid, &document));
                selected = WindowsShellDispatch.Dispatch(document, "SelectedItems", 1);
                if (selected == 0) return [];
                var count = checked((int)WindowsShellDispatch.Integer(selected, "Count"));
                var paths = new List<string>(count);
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = WindowsShellDispatch.Dispatch(selected, "Item", 3, index);
                    if (item == 0) continue;
                    try
                    {
                        var path = WindowsShellDispatch.Text(item, "Path");
                        if (Path.IsPathFullyQualified(path) && (File.Exists(path) || Directory.Exists(path)))
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
                WindowsShellDispatch.Release(selected);
                WindowsShellDispatch.Release(document);
                WindowsShellDispatch.Release(view);
                WindowsShellDispatch.Release(shellBrowser);
                WindowsShellDispatch.Release(provider);
            }
        }

        [LibraryImport("ole32.dll")]
        private static partial int CoInitializeEx(nint reserved, uint flags);

        [LibraryImport("ole32.dll")]
        private static partial void CoUninitialize();

        [LibraryImport("ole32.dll")]
        private static partial int CoEnableCallCancellation(nint reserved);

        [LibraryImport("ole32.dll")]
        private static partial int CoDisableCallCancellation(nint reserved);

        [LibraryImport("ole32.dll")]
        private static partial int CoCancelCall(uint thread, uint timeout);

        [LibraryImport("ole32.dll")]
        private static unsafe partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid,
            nint* instance);

        [LibraryImport("kernel32.dll")]
        private static partial uint GetCurrentThreadId();

        [LibraryImport("user32.dll")]
        private static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

        [LibraryImport("user32.dll", EntryPoint = "GetGUIThreadInfo")]
        private static partial int GetGuiThreadInfo(uint thread, ref WindowsShellThreadInfo information);

        [LibraryImport("user32.dll")]
        private static partial nint GetAncestor(nint window, uint flags);

        [LibraryImport("user32.dll")]
        private static partial int IsWindowVisible(nint window);

        [LibraryImport("user32.dll")]
        private static unsafe partial int GetClassNameW(nint window, char* name, int count);
    }
}
