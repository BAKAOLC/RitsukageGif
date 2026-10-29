using System.Globalization;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task<CommandReply> ExecuteInputCommandAsync(string[] args)
        {
            switch (args)
            {
                case ["list"]:
                    break;
                case ["stop", var name]:
                    await _inputServer.StopAsync(name);
                    break;
                case ["listen", var name, .. var settings]:
                {
                    var options = new InputChannelOptions(name);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    if (settings.Length % 2 != 0) throw new ArgumentException(UiText.Get("CommandHelp"));
                    for (var index = 0; index < settings.Length; index += 2)
                    {
                        var key = settings[index];
                        var value = settings[index + 1];
                        if (!seen.Add(key)) throw new ArgumentException(UiText.Get("CommandHelp"));
                        options = key switch
                        {
                            "--transport" => options with { Transport = value },
                            "--endpoint" => options with { Endpoint = value },
                            "--format" => options with { Format = value },
                            "--width" => options with { Width = int.Parse(value, CultureInfo.InvariantCulture) },
                            "--height" => options with { Height = int.Parse(value, CultureInfo.InvariantCulture) },
                            "--idle-timeout" => options with
                            {
                                IdleTimeoutSeconds = int.Parse(value, CultureInfo.InvariantCulture),
                            },
                            _ => throw new ArgumentException(UiText.Get("CommandHelp")),
                        };
                    }

                    _inputServer.Start(options);
                    break;
                }
                default:
                    throw new ArgumentException(UiText.Get("CommandHelp"));
            }

            return new(true, CurrentState, Inputs: _inputServer.List(), InputClock: ExternalInputHub.Now);
        }
    }
}
