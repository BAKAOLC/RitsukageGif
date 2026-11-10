using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal static class PinAnnotationScriptLibrary
    {
        internal static void Register(ScriptTableBinding pins, PinnedImageManager? manager = null,
            AnnotationScriptService? service = null)
        {
            pins.Function("annotate", async (args, token) =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (manager is null || service is null)
                        throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    var canvas = await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        return manager.CreateAutomationCanvas((string)args[0]!, service);
                    }, DispatcherPriority.Normal, token);
                    return new ScriptResult(true, canvas);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, error is KeyNotFoundException
                        ? ScriptServiceErrors.Create("notFound", error.Message)
                        : ScriptServiceErrors.FromException(error));
                }
            }, [new("id", "string")], "Result<AnnotationCanvas,ServiceError>");
        }
    }
}
