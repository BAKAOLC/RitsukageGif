using System.Globalization;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal static partial class MacWindowSystem
    {
        internal static (bool Exists, bool Visible, uint ProcessId) LiveWindowState(nint window)
        {
            lock (Sync)
            {
                if (_server is null || _server.HasExited)
                {
                    _updated = 0;
                    List();
                }

                try
                {
                    _server!.StandardInput.WriteLine("window-state " +
                                                     ((long)window).ToString(CultureInfo.InvariantCulture));
                    _server.StandardInput.Flush();
                    var line = _server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3))
                        .GetAwaiter().GetResult() ?? throw new IOException(UiText.Get("CaptureFailed"));
                    using var document = JsonDocument.Parse(line);
                    var state = document.RootElement;
                    return (state.GetProperty("exists").GetBoolean(), state.GetProperty("visible").GetBoolean(),
                        state.GetProperty("pid").GetUInt32());
                }
                catch
                {
                    CloseCore();
                    throw;
                }
            }
        }
    }
}
