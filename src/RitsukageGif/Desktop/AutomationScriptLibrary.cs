namespace RitsukageGif.Desktop
{
    internal static class AutomationScriptLibrary
    {
        internal static ScriptBindingRegistry CreateBindings(string? baseDirectory = null)
        {
            var bindings = CaptureScriptLibrary.CreateBindings(baseDirectory: baseDirectory);
            Register(bindings, baseDirectory: baseDirectory);
            return bindings;
        }

        internal static void Register(ScriptBindingRegistry bindings,
            Func<GlobalActionBinding, CancellationToken, Task<ScreenshotInvocation?>>? execute = null,
            Func<IReadOnlyList<string>, CancellationToken, Task>? pinFiles = null,
            ISettingsAutomationService? settings = null, string? baseDirectory = null,
            Func<CancellationToken, Task>? requestExit = null,
            AnnotationScriptService? annotation = null, IAnimationEditingService? animation = null,
            ILongCaptureService? longCapture = null, IPinAutomationService? pins = null,
            ITextTranslationService? translation = null,
            Func<IReadOnlyList<GlobalActionBinding>>? savedActions = null,
            Func<CancellationToken, Task>? openSettings = null,
            Func<string>? logDirectory = null, Action? openLogs = null,
            IClipboardAutomationService? clipboard = null, PinnedImageManager? pinManager = null)
        {
            ActionScriptLibrary.Register(bindings, execute, pinFiles, savedActions);
            SettingsScriptLibrary.Register(bindings, settings, baseDirectory);
            AnnotationScriptLibrary.Register(bindings, annotation);
            AnimationScriptLibrary.Register(bindings, animation, baseDirectory);
            LongCaptureScriptLibrary.Register(bindings, longCapture, baseDirectory);
            var pinsTable = PinScriptLibrary.Register(bindings, pins, baseDirectory);
            PinAnnotationScriptLibrary.Register(pinsTable, pinManager, annotation);
            ClipboardScriptLibrary.Register(bindings, clipboard, baseDirectory);
            TranslationScriptLibrary.Register(bindings, translation);
            var application = bindings.RegisterTable("app");
            application.Function("openSettings", async (_, token) =>
            {
                if (openSettings is null) throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                await openSettings(token);
                return null;
            }, [], "void", "Open the settings window without showing the main window.");
            application.Function("logDirectory", (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (logDirectory is null) throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                return ValueTask.FromResult<object?>(logDirectory());
            }, [], "string");
            application.Function("openLogs", (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (openLogs is null) throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                openLogs();
                return ValueTask.FromResult<object?>(null);
            }, [], "void");
            application.Function("exit", async (_, token) =>
            {
                if (requestExit is null) throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                await requestExit(token);
                return null;
            }, [], "void", "Request exit after the current script completes and its command reply is written.");
        }
    }
}
