using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Channels;

namespace RitsukageGif.Desktop
{
    internal sealed partial class InputChannelServer(ExternalInputHub hub) : IAsyncDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private readonly Dictionary<string, Channel> _channels = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();
        private bool _disposed;
        private long _incomingBytes;

        public async ValueTask DisposeAsync()
        {
            Channel[] channels;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                channels = [.. _channels.Values];
                _channels.Clear();
            }

            await Task.WhenAll(channels.Select(BeginStop)).ConfigureAwait(false);
            foreach (var channel in channels) hub.RemoveChannel(channel.Options.Name);
        }

        public void Start(InputChannelOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(options.Name) || options.Name.Length > 128 || options.Name.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')) ||
                options.Transport is not ("pipe" or "tcp") || options.Format is not ("framed" or "mpegts") ||
                options.IdleTimeoutSeconds is < 1 or > 300 ||
                (options.Format == "mpegts" && (options.Width <= 0 || options.Height <= 0 || options.Width > 16384 ||
                                                options.Height > 16384 ||
                                                (long)options.Width * options.Height * 4 > 64L * 1024 * 1024)))
                throw new ArgumentException(UiText.Get("InvalidRecording"));
            if (options.Format == "mpegts" && FfmpegEncoder.FindExecutable() is null)
                throw new FileNotFoundException(UiText.Get("FfmpegMissing"));
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_channels.Count >= 16 || _channels.ContainsKey(options.Name))
                    throw new InvalidOperationException(UiText.Get("InvalidRecording"));
                var channel = new Channel(options);
                try
                {
                    if (options.Transport == "pipe")
                    {
                        channel.Endpoint = options.Endpoint ?? "rginput-" + Guid.NewGuid().ToString("N");
                        if (channel.Endpoint.Length is 0 or > 128 || channel.Endpoint.Any(character =>
                                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
                            throw new ArgumentException(nameof(options.Endpoint));
                        channel.Pipe = CreatePipe(channel.Endpoint);
                    }
                    else
                    {
                        var endpoint = ParseEndpoint(options.Endpoint);
                        channel.Listener = new(endpoint);
                        channel.Listener.Start(1);
                        channel.Endpoint = channel.Listener.LocalEndpoint.ToString()!;
                    }

                    _channels.Add(options.Name, channel);
                    channel.Work = Task.Run(() => ListenAsync(channel));
                }
                catch
                {
                    channel.Pipe?.Dispose();
                    channel.Listener?.Stop();
                    channel.Stop.Dispose();
                    throw;
                }
            }
        }

        public IReadOnlyList<InputChannelInfo> List()
        {
            lock (_gate)
                return [.. _channels.Values.Select(channel => channel.Info(hub.GetStatus(channel.Options.Name)))];
        }

        public async Task StopAsync(string name)
        {
            Channel? channel;
            lock (_gate)
                if (!_channels.TryGetValue(name, out channel))
                    return;
            await BeginStop(channel).ConfigureAwait(false);
            lock (_gate)
                if (_channels.TryGetValue(name, out var current) && ReferenceEquals(current, channel))
                {
                    _channels.Remove(name);
                    hub.RemoveChannel(name);
                }
        }

        private Task BeginStop(Channel channel)
        {
            lock (_gate) return channel.StopWork ??= StopChannelAsync(channel);
        }

        private static async Task StopChannelAsync(Channel channel)
        {
            await channel.Stop.CancelAsync().ConfigureAwait(false);
            channel.Listener?.Stop();
            if (channel.Pipe is { } pipe) await pipe.DisposeAsync().ConfigureAwait(false);
            await channel.Work.ConfigureAwait(false);
            channel.Stop.Dispose();
        }

        private static NamedPipeServerStream CreatePipe(string name)
        {
            return new(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 65536, 65536);
        }

        private static IPEndPoint ParseEndpoint(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new(IPAddress.Loopback, 0);
            if (int.TryParse(text, out var port) && port is >= 0 and <= 65535)
                return new(IPAddress.Loopback, port);
            if (IPEndPoint.TryParse(text, out var endpoint) && IPAddress.IsLoopback(endpoint.Address)) return endpoint;
            throw new ArgumentException("Only loopback TCP endpoints are supported.", nameof(text));
        }

        private async Task ListenAsync(Channel channel)
        {
            var token = channel.Stop.Token;
            while (!token.IsCancellationRequested)
                try
                {
                    if (channel.Options.Transport == "pipe")
                    {
                        channel.Pipe ??= CreatePipe(channel.Endpoint);
                        await using var pipe = channel.Pipe;
                        await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                        await ServeAsync(channel, pipe, token).ConfigureAwait(false);
                        channel.Pipe = null;
                    }
                    else
                    {
                        using var client = await channel.Listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                        client.NoDelay = true;
                        await ServeAsync(channel, client.GetStream(), token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or SocketException
                                                  or ObjectDisposedException or
                                                  ArgumentException or InvalidOperationException or TimeoutException
                                                  or OperationCanceledException
                                                  or ChannelClosedException
                                                  or Win32Exception)
                {
                    if (token.IsCancellationRequested) break;
                    channel.Error = error.Message;
                }
                finally
                {
                    channel.Connected = false;
                    if (channel.Options.Transport == "pipe")
                    {
                        if (channel.Pipe is { } pipe) await pipe.DisposeAsync().ConfigureAwait(false);
                        channel.Pipe = null;
                    }
                }
        }

        private async Task ServeAsync(Channel channel, Stream stream, CancellationToken token)
        {
            await using var close = token.Register(stream.Dispose);
            using var publisher = hub.OpenPublisher(channel.Options.Name);
            channel.Connected = true;
            channel.Error = null;
            if (channel.Options.Format == "mpegts")
            {
                await ReadMpegTsAsync(channel, stream, publisher, token).ConfigureAwait(false);
                return;
            }

            var greeting = new byte[16];
            "RGIH"u8.CopyTo(greeting);
            BinaryPrimitives.WriteUInt32LittleEndian(greeting.AsSpan(4), 1);
            BinaryPrimitives.WriteDoubleLittleEndian(greeting.AsSpan(8), ExternalInputHub.Now);
            await stream.WriteAsync(greeting, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            var header = new byte[32];
            while (await ReadExactAsync(stream, header, channel.Options.IdleTimeoutSeconds, token)
                       .ConfigureAwait(false))
            {
                if (!header.AsSpan(0, 4).SequenceEqual("RGIP"u8) || header[4] != 1 ||
                    header[5] is not (1 or 2) || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6)) != 0 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20)) != 0)
                    throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
                var width = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
                var height = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
                var timestamp = BinaryPrimitives.ReadDoubleLittleEndian(header.AsSpan(24));
                if (!double.IsFinite(timestamp) || timestamp < 0 || timestamp > ExternalInputHub.Now + 5 ||
                    count == 0 || count > 64 * 1024 * 1024 ||
                    (header[5] == 1 && (width == 0 || height == 0 || width > 16384 || height > 16384 ||
                                        (long)width * height * 4 != count)) ||
                    (header[5] == 2 && (count > 1024 * 1024 || width != 0 || height != 0)))
                    throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
                var payload = RentPayload((int)count);
                try
                {
                    if (!await ReadExactAsync(stream, payload.AsMemory(0, (int)count),
                            channel.Options.IdleTimeoutSeconds, token).ConfigureAwait(false))
                        throw new EndOfStreamException();
                    var publishedAt = timestamp == 0 ? ExternalInputHub.Now : timestamp;
                    var accepted = header[5] == 1
                        ? publisher.TryPublishFrame((int)width, (int)height, payload.AsSpan(0, (int)count), publishedAt)
                        : publisher.TryPublishJson(StrictUtf8.GetString(payload, 0, (int)count), publishedAt);
                    if (accepted)
                    {
                        Interlocked.Increment(ref channel.Packets);
                        Volatile.Write(ref channel.Timestamp, publishedAt);
                    }
                }
                finally
                {
                    ReturnPayload(payload);
                }
            }
        }

        private byte[] RentPayload(int size)
        {
            var reservation = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(16, size));
            if (Interlocked.Add(ref _incomingBytes, reservation) > 128L * 1024 * 1024)
            {
                Interlocked.Add(ref _incomingBytes, -reservation);
                throw new InvalidDataException(UiText.Get("MemoryLimit"));
            }

            try
            {
                return ArrayPool<byte>.Shared.Rent(reservation);
            }
            catch
            {
                Interlocked.Add(ref _incomingBytes, -reservation);
                throw;
            }
        }

        private void ReturnPayload(byte[] payload)
        {
            ArrayPool<byte>.Shared.Return(payload);
            Interlocked.Add(ref _incomingBytes, -payload.Length);
        }

        private static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> buffer, int timeout,
            CancellationToken token)
        {
            var offset = 0;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
            while (offset < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer[offset..], deadline.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    if (offset == 0) return false;
                    throw new EndOfStreamException();
                }

                offset += count;
            }

            return true;
        }

        private sealed class Channel(InputChannelOptions options)
        {
            public readonly InputChannelOptions Options = options;
            public readonly CancellationTokenSource Stop = new();
            public volatile bool Connected;
            public string Endpoint = "";
            public string? Error;
            public TcpListener? Listener;
            public long Packets;
            public NamedPipeServerStream? Pipe;
            public Task? StopWork;
            public double Timestamp = double.NaN;
            public Task Work = Task.CompletedTask;

            public InputChannelInfo Info(ExternalInputChannelStatus? status)
            {
                return new(Options.Name, Options.Transport,
                    Endpoint, Options.Format,
                    Connected, Interlocked.Read(ref Packets),
                    double.IsFinite(Volatile.Read(ref Timestamp)) ? Volatile.Read(ref Timestamp) : null, Error, status);
            }
        }
    }
}
