using System.IO.Pipes;

namespace RitsukageGif.Core
{
    internal sealed class VideoAudioPipe : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly IAudioCaptureSource _source;

        public VideoAudioPipe(IAudioCaptureSource source)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            source.Format.Validate();
            _source = source;
            var name = "ritsukage-audio-" + Guid.NewGuid().ToString("N");
            Path = @"\\.\pipe\" + name;
            _pipe = new(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 0, 65536);
        }

        public string Path { get; }
        public AudioCaptureFormat Format => _source.Format;

        public void Dispose()
        {
            _pipe.Dispose();
        }

        public async Task RunAsync(CancellationToken token)
        {
            var connection = _pipe.WaitForConnectionAsync(token);
            try
            {
                await _source.RunAsync(async (bytes, cancellation) =>
                {
                    await connection.WaitAsync(cancellation);
                    await _pipe.WriteAsync(bytes, cancellation);
                }, token);
            }
            finally
            {
                if (!connection.IsCompleted) await _pipe.DisposeAsync();
                try
                {
                    await connection;
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException
                                                      or ObjectDisposedException)
                {
                }
            }
        }
    }
}
