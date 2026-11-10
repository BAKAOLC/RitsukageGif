namespace RitsukageGif.Desktop
{
    internal sealed partial class MacScreenshotPasteTarget(nint system, nint application, nint focus, int processId)
        : IScreenshotPasteTarget
    {
        private nint _system = system;
        private nint _application = application;
        private nint _focus = focus;
        private readonly int _processId = processId;
        private nint _clipboardCount;
        private bool _copied;

        public ValueTask MarkClipboardCopiedAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _clipboardCount = ReadClipboardCount();
            _copied = true;
            return ValueTask.CompletedTask;
        }

        public async Task TryPasteAsync(CancellationToken token)
        {
            if (!_copied || _focus == 0) return;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (ReadClipboardCount() != _clipboardCount) return;
                if (ModifiersReleased()) break;
                if (attempt == 19) return;
                await Task.Delay(15, token);
            }

            var matches = await Task.Run(MatchesFocus, token);
            token.ThrowIfCancellationRequested();
            if (!matches || !ModifiersReleased() || ReadClipboardCount() != _clipboardCount) return;
            PostPaste();
        }

        public void Dispose()
        {
            Release(Interlocked.Exchange(ref _focus, 0));
            Release(Interlocked.Exchange(ref _application, 0));
            Release(Interlocked.Exchange(ref _system, 0));
        }

        internal static MacScreenshotPasteTarget? Capture()
        {
            if (!AxIsProcessTrusted() || !CgPreflightPostEventAccess()) return null;
            var system = AXUIElementCreateSystemWide();
            nint application = 0;
            nint focus = 0;
            try
            {
                if (system == 0 || AXUIElementSetMessagingTimeout(system, 0.25f) != 0) return null;
                application = ReadAttribute(system, "AXFocusedApplication");
                if (application == 0 || AxuiElementGetPid(application, out var processId) != 0 ||
                    processId <= 0 || processId == Environment.ProcessId) return null;
                if (AXUIElementSetMessagingTimeout(application, 0.25f) != 0) return null;
                focus = ReadAttribute(application, "AXFocusedUIElement");
                if (focus == 0) return null;
                var target = new MacScreenshotPasteTarget(system, application, focus, processId);
                system = application = focus = 0;
                return target;
            }
            finally
            {
                Release(focus);
                Release(application);
                Release(system);
            }
        }

        private bool MatchesFocus()
        {
            using var current = Capture();
            return current is not null && current._processId == _processId &&
                   CfEqual(_application, current._application) && CfEqual(_focus, current._focus);
        }

        private static bool ModifiersReleased()
        {
            return (CGEventSourceFlagsState(0) & 0x1e0000UL) == 0;
        }

        private void PostPaste()
        {
            var commandDown = CgEventCreateKeyboardEvent(0, 55, true);
            var pasteDown = CgEventCreateKeyboardEvent(0, 9, true);
            var pasteUp = CgEventCreateKeyboardEvent(0, 9, false);
            var commandUp = CgEventCreateKeyboardEvent(0, 55, false);
            try
            {
                if (commandDown == 0 || pasteDown == 0 || pasteUp == 0 || commandUp == 0)
                    throw new InvalidOperationException("Unable to create the paste keyboard events.");
                CGEventSetFlags(commandDown, 0x100000UL);
                CGEventSetFlags(pasteDown, 0x100000UL);
                CGEventSetFlags(pasteUp, 0x100000UL);
                CGEventSetFlags(commandUp, 0);
                CGEventPostToPid(_processId, commandDown);
                CGEventPostToPid(_processId, pasteDown);
                CGEventPostToPid(_processId, pasteUp);
                CGEventPostToPid(_processId, commandUp);
            }
            finally
            {
                Release(commandUp);
                Release(pasteUp);
                Release(pasteDown);
                Release(commandDown);
            }
        }

        private static nint ReadAttribute(nint element, string name)
        {
            var attribute = CfStringCreateWithCString(0, name, 0x08000100);
            if (attribute == 0) return 0;
            try
            {
                if (AxuiElementCopyAttributeValue(element, attribute, out var value) == 0) return value;
                Release(value);
                return 0;
            }
            finally
            {
                CFRelease(attribute);
            }
        }

        private static nint ReadClipboardCount()
        {
            var pool = AutoreleasePoolPush();
            try
            {
                var board = SendPointer(GetClass("NSPasteboard"), RegisterSelector("generalPasteboard"));
                return SendPointer(board, RegisterSelector("changeCount"));
            }
            finally
            {
                AutoreleasePoolPop(pool);
            }
        }

        private static void Release(nint value)
        {
            if (value != 0) CFRelease(value);
        }
    }
}
