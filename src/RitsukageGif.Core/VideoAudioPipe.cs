using System.IO.Pipes;
using System.Net.Sockets;

namespace RitsukageGif.Core
{
    internal sealed class VideoAudioPipe : IDisposable
    {
        private readonly NamedPipeServerStream? _pipe;
        private readonly IAudioCaptureSource _source;
        private readonly Socket? _listener;
        private readonly string? _directory;

        public VideoAudioPipe(IAudioCaptureSource source)
        {
            source.Format.Validate();
            _source = source;
            if (!OperatingSystem.IsWindows())
            {
                _directory = "/tmp/rg-audio-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(_directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var socketPath = System.IO.Path.Combine(_directory, "pcm");
                Path = "unix://" + socketPath;
                _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    _listener.Bind(new UnixDomainSocketEndPoint(socketPath));
                    File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    _listener.Listen(1);
                }
                catch
                {
                    Dispose();
                    throw;
                }

                return;
            }

            var name = "ritsukage-audio-" + Guid.NewGuid().ToString("N");
            Path = @"\\.\pipe\" + name;
            _pipe = new(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 0, 65536);
        }

        public string Path { get; }
        public AudioCaptureFormat Format => _source.Format;

        public void Dispose()
        {
            _pipe?.Dispose();
            _listener?.Dispose();
            if (_directory is not null && Directory.Exists(_directory))
            {
                File.Delete(System.IO.Path.Combine(_directory, "pcm"));
                Directory.Delete(_directory);
            }
        }

        public async Task RunAsync(CancellationToken token, RecordingControl? control = null)
        {
            if (_listener is not null)
            {
                var accepted = _listener.AcceptAsync(token).AsTask();
                NetworkStream? stream = null;
                try
                {
                    await _source.RunAsync(async (bytes, cancellation) =>
                    {
                        if (control?.IsPaused == true) return;
                        var generation = control?.Generation ?? 0;
                        if (stream is null)
                        {
                            var connection = await accepted.WaitAsync(cancellation);
                            stream = new(connection, true);
                            _listener.Dispose();
                        }

                        if (control is not null && !control.IsCurrentCapture(generation)) return;
                        await stream.WriteAsync(bytes, cancellation);
                    }, token);
                }
                finally
                {
                    _listener.Dispose();
                    if (stream is not null) await stream.DisposeAsync();
                    else
                        try
                        {
                            (await accepted).Dispose();
                        }
                        catch (Exception exception) when (exception is OperationCanceledException or SocketException
                                                              or ObjectDisposedException)
                        {
                        }
                }

                return;
            }

            var pipe = _pipe!;
            var connectionTask = pipe.WaitForConnectionAsync(token);
            try
            {
                await _source.RunAsync(async (bytes, cancellation) =>
                {
                    if (control?.IsPaused == true) return;
                    var generation = control?.Generation ?? 0;
                    await connectionTask.WaitAsync(cancellation);
                    if (control is not null && !control.IsCurrentCapture(generation)) return;
                    await pipe.WriteAsync(bytes, cancellation);
                }, token);
            }
            finally
            {
                await pipe.DisposeAsync();
                try
                {
                    await connectionTask;
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException
                                                      or ObjectDisposedException)
                {
                }
            }
        }
    }
}
