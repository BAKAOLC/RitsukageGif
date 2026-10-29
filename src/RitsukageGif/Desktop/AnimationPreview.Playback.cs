using System.Threading.Channels;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationPreview
    {
        private sealed class Playback : IDisposable
        {
            private const long CacheBytes = 16L * 1024 * 1024;
            private const int CacheFrames = 256;
            private readonly CancellationTokenSource _cancel = new();
            private readonly CancellationToken _token;
            private readonly Task _worker;
            private int _disposed;

            public Playback(string path)
            {
                _token = _cancel.Token;
                _worker = Task.Run(() => DecodeAsync(path), CancellationToken.None);
            }

            public CancellationToken Token => _token;

            public Channel<PreviewFrame> Frames { get; } = Channel.CreateBounded<PreviewFrame>(
                new BoundedChannelOptions(2)
                    { SingleReader = false, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _cancel.Cancel();
                _ = _worker.ContinueWith(_ =>
                {
                    while (Frames.Reader.TryRead(out var frame)) frame.Dispose();
                    _cancel.Dispose();
                }, TaskScheduler.Default);
            }

            private async Task DecodeAsync(string path)
            {
                var cached = new List<PreviewFrame>();
                (long Length, DateTime Modified)? stamp = null;
                var repeatCache = false;
                try
                {
                    while (!_token.IsCancellationRequested)
                    {
                        var currentStamp = ReadStamp(path);
                        if (repeatCache && currentStamp == stamp)
                        {
                            foreach (var frame in cached)
                                await WriteAsync(frame.Retain());
                            continue;
                        }

                        ClearCache();
                        stamp = currentStamp;
                        var canCache = stamp.HasValue;
                        long bytes = 0;
                        var count = 0;
                        foreach (var frame in PreviewDecoder.Read(path, _token, OperatingSystem.IsWindows()))
                        {
                            try
                            {
                                if (canCache)
                                {
                                    if (cached.Count < CacheFrames && bytes + frame.StorageBytes <= CacheBytes)
                                    {
                                        cached.Add(frame.Retain());
                                        bytes += frame.StorageBytes;
                                    }
                                    else
                                    {
                                        ClearCache();
                                        canCache = false;
                                    }
                                }
                            }
                            catch
                            {
                                frame.Dispose();
                                throw;
                            }

                            await WriteAsync(frame);
                            count++;
                        }

                        if (count == 0) throw new InvalidDataException(UiText.Get("NoFrames"));
                        if (count == 1) break;
                        repeatCache = canCache && ReadStamp(path) == stamp;
                        if (!repeatCache) ClearCache();
                    }

                    Frames.Writer.TryComplete();
                }
                catch (OperationCanceledException) when (_token.IsCancellationRequested)
                {
                    Frames.Writer.TryComplete();
                }
                catch (Exception error)
                {
                    Frames.Writer.TryComplete(error);
                }
                finally
                {
                    ClearCache();
                }

                return;

                void ClearCache()
                {
                    foreach (var frame in cached) frame.Dispose();
                    cached.Clear();
                }
            }

            private async ValueTask WriteAsync(PreviewFrame frame)
            {
                try
                {
                    await Frames.Writer.WriteAsync(frame, _token);
                }
                catch
                {
                    frame.Dispose();
                    throw;
                }
            }

            private static (long Length, DateTime Modified)? ReadStamp(string path)
            {
                var file = new FileInfo(path);
                return file.Exists ? (file.Length, file.LastWriteTimeUtc) : null;
            }
        }
    }
}
