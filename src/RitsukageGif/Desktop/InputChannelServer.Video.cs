using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace RitsukageGif.Desktop
{
    internal sealed partial class InputChannelServer
    {
        private async Task ReadMpegTsAsync(Channel channel, Stream stream, ExternalInputPublisher publisher,
            CancellationToken token)
        {
            var executable = FfmpegEncoder.FindExecutable() ??
                             throw new FileNotFoundException(UiText.Get("FfmpegMissing"));
            using var process = new Process();
            process.StartInfo = new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-loglevel", "info", "-nostdin", "-probesize", "2048", "-analyzeduration", "1",
                         "-threads", "1", "-f", "mpegts", "-i", "pipe:0",
                         "-map", "0:v:0", "-an", "-sn", "-dn", "-vf",
                         $"scale={channel.Options.Width}:{channel.Options.Height},showinfo", "-fps_mode", "passthrough",
                         "-threads", "1", "-pix_fmt", "bgra", "-flush_packets", "1", "-f", "rawvideo", "pipe:1",
                     }) process.StartInfo.ArgumentList.Add(argument);
            token.ThrowIfCancellationRequested();
            process.Start();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            var active = lifetime.Token;
            await using var stop = active.Register(static state => KillDecoder((Process)state!), process);
            var timestamps = System.Threading.Channels.Channel.CreateBounded<double>(new BoundedChannelOptions(64)
                { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            var errors = ReadTimestampsAsync(process.StandardError, timestamps.Writer, active);
            var input = FeedDecoderAsync(stream, process.StandardInput.BaseStream, channel.Options.IdleTimeoutSeconds,
                active);
            var length = checked(channel.Options.Width * channel.Options.Height * 4);
            byte[]? pixels = null;
            double? origin = null;
            double? lastPts = null;
            var lastTime = 0d;
            try
            {
                pixels = RentPayload(length);
                while (await ReadExactAsync(process.StandardOutput.BaseStream, pixels.AsMemory(0, length),
                           channel.Options.IdleTimeoutSeconds, active).ConfigureAwait(false))
                {
                    var pts = await timestamps.Reader.ReadAsync(active).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(channel.Options.IdleTimeoutSeconds), active)
                        .ConfigureAwait(false);
                    if (origin is null || pts < lastPts)
                        origin = Math.Max(ExternalInputHub.Now, lastTime + .000001) - pts;
                    var timestamp = origin.Value + pts;
                    var ahead = timestamp - ExternalInputHub.Now;
                    switch (ahead)
                    {
                        case > 5: throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                        case > 0: await Task.Delay(TimeSpan.FromSeconds(ahead), active).ConfigureAwait(false); break;
                    }

                    if (publisher.TryPublishFrame(channel.Options.Width, channel.Options.Height,
                            pixels.AsSpan(0, length), timestamp))
                    {
                        Interlocked.Increment(ref channel.Packets);
                        Volatile.Write(ref channel.Timestamp, timestamp);
                        lastTime = timestamp;
                    }

                    lastPts = pts;
                }

                await input.ConfigureAwait(false);
                await process.WaitForExitAsync(active).ConfigureAwait(false);
                await errors.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new IOException(UiText.Get("InvalidFrameBuffer"));
            }
            finally
            {
                if (pixels is not null) ReturnPayload(pixels);
                await lifetime.CancelAsync().ConfigureAwait(false);
                KillDecoder(process);
                await stream.DisposeAsync().ConfigureAwait(false);
                try
                {
                    await Task.WhenAll(input, errors).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException
                                                  or ObjectDisposedException)
                {
                }
            }
        }

        private static async Task FeedDecoderAsync(Stream source, Stream destination, int timeout,
            CancellationToken token)
        {
            var buffer = new byte[65536];
            try
            {
                while (true)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
                    var count = await source.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                await destination.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task ReadTimestampsAsync(StreamReader reader, ChannelWriter<double> timestamps,
            CancellationToken token)
        {
            var line = new StringBuilder(1024);
            var buffer = new char[1024];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                    for (var index = 0; index < count; index++)
                        if (buffer[index] == '\n')
                        {
                            var value = line.ToString();
                            line.Clear();
                            var marker = value.IndexOf("pts_time:", StringComparison.Ordinal);
                            if (marker < 0 || !value.Contains("showinfo", StringComparison.Ordinal)) continue;
                            var number = value.AsMemory(marker + 9);
                            var end = number.Span.IndexOf(' ');
                            if (end >= 0) number = number[..end];
                            if (double.TryParse(number.Span, NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out var pts) &&
                                double.IsFinite(pts))
                                await timestamps.WriteAsync(pts, token).ConfigureAwait(false);
                        }
                        else if (line.Length < 8192)
                        {
                            line.Append(buffer[index]);
                        }
            }
            finally
            {
                timestamps.TryComplete();
            }
        }

        private static void KillDecoder(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }
    }
}
