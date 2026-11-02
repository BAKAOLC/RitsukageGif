using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed class LinuxTrayIcon : PortableTrayIcon
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Tray");
        private readonly CancellationTokenSource _stop = new();

        private readonly Channel<string> _updates = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest,
        });
        private readonly string _executable;
        private Process? _process;

        private string _icon = "ICON 1 1 AAAAAA==", _tooltip = "-";
        private bool _visible = true;

        internal LinuxTrayIcon()
        {
            _executable = Path.Combine(AppContext.BaseDirectory, "ritsukagegif-tray");
            if (!File.Exists(_executable))
                throw new FileNotFoundException("The tray helper was not found.", _executable);
            Completion = RunAsync();
        }

        internal override bool SupportsDoubleClick => false;
        internal override Task Completion { get; }

        public override void SetIcon(IWindowIconImpl? icon)
        {
            if (icon is null)
            {
                _icon = "ICON 1 1 AAAAAA==";
            }
            else
            {
                using var stream = new MemoryStream();
                icon.Save(stream);
                using var bitmap = SKBitmap.Decode(stream.ToArray()) ??
                                   throw new InvalidDataException("The tray icon could not be decoded.");
                var colors = bitmap.Pixels;
                var bytes = new byte[checked(colors.Length * 4)];
                for (var index = 0; index < colors.Length; index++)
                {
                    var color = colors[index];
                    bytes[index * 4] = color.Alpha;
                    bytes[index * 4 + 1] = color.Red;
                    bytes[index * 4 + 2] = color.Green;
                    bytes[index * 4 + 3] = color.Blue;
                }

                _icon = FormattableString.Invariant(
                    $"ICON {bitmap.Width} {bitmap.Height} {Convert.ToBase64String(bytes)}");
            }

            PublishMenu();
        }

        public override void SetToolTipText(string? text)
        {
            var encoded = Encode(text ?? "");
            if (_tooltip == encoded) return;
            _tooltip = encoded;
            PublishMenu();
        }

        public override void SetIsVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            PublishMenu();
        }

        protected override void PublishMenu()
        {
            if (IsDisposed) return;
            var update = new StringBuilder("RESET\n");
            foreach (var entry in MenuModel.Capture())
                update.Append(CultureInfo.InvariantCulture,
                    $"ITEM {entry.Id} {entry.Parent} {(entry.Enabled ? 1 : 0)} {entry.Toggle} {(entry.Checked ? 1 : 0)} {(entry.Separator ? 1 : 0)} {Encode(entry.Label)} {Encode(TrayGestureEncoding.LinuxKeys(entry.Gesture))}\n");
            update.Append(_icon).Append('\n').Append("TEXT ").Append(_tooltip).Append('\n')
                .Append("VISIBLE ").Append(_visible ? '1' : '0').Append("\nCOMMIT\n");
            _updates.Writer.TryWrite(update.ToString());
        }

        protected override void DisposeNative()
        {
            TrayContextPopup.Close();
            _stop.Cancel();
            _updates.Writer.TryComplete();
            try
            {
                _process?.StandardInput.BaseStream.Close();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                Logger.ZLogDebug($"Tray helper input was already closed. Error={exception.Message}");
            }
        }

        private async Task RunAsync()
        {
            var delay = 250;
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        using var process = Process.Start(new ProcessStartInfo(_executable)
                        {
                            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                            RedirectStandardOutput = true, RedirectStandardError = true,
                        }) ?? throw new IOException("The tray helper could not be started.");
                        _process = process;
                        using var session = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                        var writing = WriteAsync(process, session.Token);
                        var errors = ReadErrorsAsync(process, session.Token);
                        try
                        {
                            while (await process.StandardOutput.ReadLineAsync(session.Token).ConfigureAwait(false) is
                                   { } line)
                                Receive(line);
                        }
                        finally
                        {
                            await session.CancelAsync().ConfigureAwait(false);
                            try
                            {
                                await process.StandardInput.BaseStream.DisposeAsync().ConfigureAwait(false);
                            }
                            catch (Exception exception) when (exception is IOException or InvalidOperationException)
                            {
                            }

                            try
                            {
                                await Task.WhenAll(writing, errors).ConfigureAwait(false);
                            }
                            catch (Exception exception) when (exception is OperationCanceledException or IOException
                                                                  or ObjectDisposedException)
                            {
                            }

                            if (!process.HasExited) process.Kill();
                            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                            _process = null;
                            Post(() =>
                            {
                                TrayContextPopup.Close();
                                IsReady = HostAvailable = false;
                                PortableTrayState.Notify();
                            });
                        }
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        Logger.ZLogWarning(exception, $"Tray helper disconnected; reconnecting.");
                    }

                    await Task.Delay(delay, _stop.Token).ConfigureAwait(false);
                    delay = Math.Min(10000, delay * 2);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private async Task WriteAsync(Process process, CancellationToken token)
        {
            await foreach (var update in _updates.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                await process.StandardInput.WriteAsync(update.AsMemory(), token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
            }
        }

        private static async Task ReadErrorsAsync(Process process, CancellationToken token)
        {
            while (await process.StandardError.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                Logger.ZLogDebug($"Tray helper diagnostic. Message={line}");
        }

        private void Receive(string line)
        {
            var split = line.Split(' ', 3);
            switch (split[0])
            {
                case "READY":
                    Post(() =>
                    {
                        IsReady = true;
                        PortableTrayState.Notify();
                        PublishMenu();
                    });
                    break;
                case "HOST":
                    var available = split[1] == "1";
                    Post(() =>
                    {
                        if (!available) TrayContextPopup.Close();
                        HostAvailable = available;
                        PortableTrayState.Notify();
                    });
                    break;
                case "ACTIVATE": Post(() => Click(TrayMouseGesture.SingleClick)); break;
                case "SECONDARY": Post(() => Click(TrayMouseGesture.MiddleClick)); break;
                case "CLICK":
                    var item = int.Parse(split[1], CultureInfo.InvariantCulture);
                    Post(() => MenuModel.Invoke(item));
                    break;
                case "ABOUT":
                    var menu = int.Parse(split[1], CultureInfo.InvariantCulture);
                    Post(() => MenuModel.RequestUpdate(menu));
                    break;
                case "CONTEXT":
                    var x = int.Parse(split[1], CultureInfo.InvariantCulture);
                    var y = int.Parse(split[2], CultureInfo.InvariantCulture);
                    Post(() => TrayContextPopup.Show(MenuModel, x, y));
                    break;
                case "ERROR":
                    var error = Encoding.UTF8.GetString(Convert.FromBase64String(split[1]));
                    Logger.ZLogWarning($"Tray helper reported an error. Message={error}");
                    break;
            }
        }

        private void Post(Action action)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsDisposed) action();
            });
        }

        private static string Encode(string value)
        {
            return value.Length == 0 ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }
    }
}
