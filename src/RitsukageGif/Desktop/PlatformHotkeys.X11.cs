namespace RitsukageGif.Desktop
{
    internal static partial class PlatformHotkeys
    {
        private static void RunX11((uint Modifiers, uint Key)[] keys, Action<int> pressed,
            Action<string> error, Action registered, Action<int, string?>? registrationChanged,
            CancellationToken token)
        {
            var delay = 1;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    RunPortableHotkeysAsync(keys, pressed, error, () =>
                        {
                            delay = 1;
                            registered();
                        }, registrationChanged, null, "RitsukageGif.X11Shortcuts", false, null, token)
                        .GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception failure)
                {
                    if (token.IsCancellationRequested) return;
                    for (var index = 0; index < keys.Length; index++)
                        if (keys[index].Key != 0)
                            registrationChanged?.Invoke(index, failure.Message);
                    error(failure.Message);
                }

                if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(delay))) return;
                delay = Math.Min(30, delay * 2);
            }
        }
    }
}
