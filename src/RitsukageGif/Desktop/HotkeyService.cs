using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class HotkeyService : IDisposable
    {
        internal static bool Supported => !DesktopCapture.UsesSourceSelection;
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread? _thread;
        private readonly IDisposable? _native;

        public HotkeyService(IReadOnlyList<(string Shortcut, Func<Task> Action)> bindings, Action<string> error)
        {
            Validate(bindings.Select(b => b.Shortcut));
            var keys = bindings.Select(b => Parse(b.Shortcut)).ToArray();
            if (OperatingSystem.IsMacOS())
            {
                _native = PlatformHotkeys.RegisterMac(keys, index => Dispatcher.UIThread.Post(() =>
                {
                    if (!_stop.IsCancellationRequested) _ = ExecuteAsync(bindings[index].Action, error);
                }), error);
                return;
            }

            _thread = new Thread(() =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        PlatformHotkeys.Run(keys, _stop.Token, index => Dispatcher.UIThread.Post(() =>
                        {
                            if (!_stop.IsCancellationRequested) _ = ExecuteAsync(bindings[index].Action, error);
                        }), message => Dispatcher.UIThread.Post(() => error(message)));
                    }
                    catch (Exception exception)
                    {
                        Dispatcher.UIThread.Post(() => error(exception.Message));
                    }

                    return;
                }

                var registered = new List<int>();
                try
                {
                    for (var i = 0; i < keys.Length; i++)
                    {
                        if (keys[i].Key == 0) continue;
                        if (NativeMethods.RegisterHotKey(0, i + 1, keys[i].Modifiers | 0x4000, keys[i].Key) != 0)
                            registered.Add(i + 1);
                        else
                        {
                            var shortcut = bindings[i].Shortcut;
                            Dispatcher.UIThread.Post(() => error(UiText.Get("HotkeyOccupied") + " " + shortcut));
                        }
                    }

                    while (!_stop.IsCancellationRequested)
                    {
                        while (NativeMethods.PeekMessageW(out var message, 0, 0x312, 0x312, 1) != 0)
                        {
                            var index = (int)message.WParam - 1;
                            if (index < 0 || index >= bindings.Count) continue;
                            var action = bindings[index].Action;
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (!_stop.IsCancellationRequested) _ = ExecuteAsync(action, error);
                            });
                        }

                        _stop.Token.WaitHandle.WaitOne(15);
                    }
                }
                finally
                {
                    foreach (var id in registered) NativeMethods.UnregisterHotKey(0, id);
                }
            }) { IsBackground = true, Name = "Global hotkeys" };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop.Cancel();
            _native?.Dispose();
            _thread?.Join();
            _stop.Dispose();
        }

        private static async Task ExecuteAsync(Func<Task> action, Action<string> error)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                error(ex.Message);
            }
        }

        public static void Validate(IEnumerable<string> shortcuts)
        {
            var seen = new HashSet<(uint, uint)>();
            foreach (var shortcut in shortcuts)
            {
                var key = Parse(shortcut);
                if (key.Key != 0 && !seen.Add(key)) throw new ArgumentException(UiText.Get("DuplicateHotkeys"));
            }
        }

        public static (uint Modifiers, uint Key) Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (0, 0);
            uint modifiers = 0, key = 0;
            foreach (var part in text.Split('+',
                         StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL": modifiers |= 2; break;
                    case "SHIFT": modifiers |= 4; break;
                    case "ALT": modifiers |= 1; break;
                    case "WIN":
                    case "WINDOWS":
                    case "CMD":
                    case "META":
                    case "SUPER": modifiers |= 8; break;
                    default:
                        if (key != 0) throw new ArgumentException(UiText.Get("HotkeySingleKey"));
                        var upper = part.ToUpperInvariant();
                        if (upper.Length == 1 && char.IsAsciiLetterOrDigit(upper[0])) key = upper[0];
                        else if (upper.StartsWith('F') && int.TryParse(upper[1..], out var f) && f is >= 1 and <= 24)
                            key = (uint)(0x70 + f - 1);
                        else if (HotkeyKeyMap.TryParse(upper, out var mapped)) key = mapped;
                        else throw new ArgumentException(UiText.Get("HotkeySupported"));
                        break;
                }

            if (key == 0) throw new ArgumentException(UiText.Get("HotkeySingleKey"));
            return (modifiers, key);
        }
    }
}
