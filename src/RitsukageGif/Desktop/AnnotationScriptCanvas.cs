using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationScriptCanvas : IAsyncDisposable
    {
        private readonly AnnotationScriptService _owner;
        private readonly ScreenshotAnnotationSession _session;
        private readonly bool _ownsSession;
        private readonly Func<CancellationToken, Task<byte[]>>? _composeOutput;
        private readonly Func<bool>? _available;
        private readonly Func<CancellationToken, Task<CapturedFrame?>>? _composeHdrOutput;
        private CapturedFrame? _hdrSource;
        private readonly SemaphoreSlim _gate = new(1);
        private readonly CancellationTokenSource _lifetime = new();
        private byte[] _source;
        private int _disposed;
        private int _count;
        private int _width;
        private int _height;
        private bool _canUndo;
        private bool _canRedo;

        internal AnnotationScriptCanvas(AnnotationScriptService owner, int width, int height, byte[] png, byte[] pixels,
            CapturedFrame? hdrSource = null)
        {
            _owner = owner;
            _width = width;
            _height = height;
            _source = png;
            _hdrSource = hdrSource;
            _session = new();
            _ownsSession = true;
            _session.NotifySelection(new(0, 0, width, height));
            _session.RegisterFrame(new(0, 0, width, height), pixels);
            _session.Changed += RefreshState;
        }

        internal AnnotationScriptCanvas(AnnotationScriptService owner, ScreenshotAnnotationSession session,
            int width, int height, Func<CancellationToken, Task<byte[]>> composeOutput, Func<bool> available,
            Func<CancellationToken, Task<CapturedFrame?>>? composeHdrOutput)
        {
            _owner = owner;
            _session = session;
            _width = width;
            _height = height;
            _source = [];
            _composeOutput = composeOutput;
            _composeHdrOutput = composeHdrOutput;
            _available = available;
            _session.Changed += RefreshState;
            RefreshState();
        }

        internal int Width => Volatile.Read(ref _width);
        internal int Height => Volatile.Read(ref _height);
        internal int Count => Volatile.Read(ref _count);
        internal bool CanUndo => Volatile.Read(ref _canUndo);
        internal bool CanRedo => Volatile.Read(ref _canRedo);
        internal bool IsClosed => Volatile.Read(ref _disposed) != 0;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            await _lifetime.CancelAsync();
            await _gate.WaitAsync();
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _session.Changed -= RefreshState;
                    try
                    {
                        if (_ownsSession) _session.Dispose();
                    }
                    finally
                    {
                        _source = [];
                        _hdrSource = null;
                        _owner.Forget(this);
                    }
                });
            }
            finally
            {
                _gate.Release();
                _lifetime.Dispose();
                _gate.Dispose();
            }
        }

        internal Task<object?> AddAsync(ScreenshotAnnotationRecord record, object? image, CancellationToken token,
            int? replaceIndex = null)
        {
            return ExecuteAsync(async cancellation =>
            {
                var watermark = image is null ? null : (await _owner.ReadAsync(image, cancellation)).ToArray();
                return (double)await _session.WriteAutomationAnnotationAsync(record, watermark, cancellation,
                    replaceIndex);
            }, token);
        }

        internal Task<object?> MoveAsync(int index, double dx, double dy, CancellationToken token)
        {
            return ExecuteAsync(async cancellation =>
                (double)await _session.MoveAutomationAnnotationAsync(index, dx, dy, cancellation), token);
        }

        internal Task<object?> EraseStrokesAsync(IReadOnlyList<AnnotationDocumentPoint> points, CancellationToken token)
        {
            return ExecuteAsync(
                cancellation => Task.FromResult<object?>((double)_session.EraseAutomationStrokes(points, cancellation)),
                token);
        }

        internal Task<object?> EditAsync(string operation, int index, CancellationToken token)
        {
            return ExecuteAsync(_ =>
            {
                switch (operation)
                {
                    case "remove": _session.RemoveAutomationAnnotation(index); break;
                    case "clear": _session.ClearAnnotations(); break;
                    case "undo": _session.Undo(); break;
                    case "redo": _session.Redo(); break;
                    default: throw new ArgumentException("Unknown annotation operation.", nameof(operation));
                }

                return Task.FromResult<object?>(null);
            }, token);
        }

        internal Task<object?> ImageAsync(CancellationToken token)
        {
            return ExecuteAsync(async cancellation => new ScriptBytes(await ComposeAsync(cancellation)), token);
        }

        internal Task<object?> ExportAsync(string output, StaticImageSaveOptions options, CancellationToken token)
        {
            return ExecuteAsync(async cancellation =>
            {
                var png = await ComposeAsync(cancellation);
                var info = await new StaticImageService().SaveAsync(png, _owner.Resolve(output), options, cancellation);
                return new Dictionary<string, object?>
                {
                    ["path"] = info.Path, ["bytes"] = (double)info.Bytes, ["width"] = (double)info.Width,
                    ["height"] = (double)info.Height, ["frames"] = (double)info.Frames,
                    ["duration"] = info.DurationSeconds, ["format"] = info.Format,
                };
            }, token);
        }

        internal Task<object?> ImageHdrAsync(CancellationToken token)
        {
            return ExecuteAsync(async cancellation => new ScriptBytes(await HdrImageService.EncodePngAsync(
                await ComposeHdrAsync(cancellation), cancellation)), token);
        }

        internal Task<object?> ExportHdrAsync(string output, HdrImageSaveOptions options, CancellationToken token)
        {
            return ExecuteAsync(async cancellation =>
            {
                var info = await HdrImageService.SaveAsync(await ComposeHdrAsync(cancellation),
                    _owner.Resolve(output), options, cancellation);
                return new Dictionary<string, object?>
                {
                    ["path"] = info.Path, ["bytes"] = (double)info.Bytes, ["width"] = (double)info.Width,
                    ["height"] = (double)info.Height, ["frames"] = (double)info.Frames,
                    ["duration"] = info.DurationSeconds, ["format"] = info.Format,
                };
            }, token);
        }

        private async Task<CapturedFrame> ComposeHdrAsync(CancellationToken token)
        {
            if (_composeHdrOutput is not null)
                return await _composeHdrOutput(token) ??
                       throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
            if (_hdrSource is not { } source)
                throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
            return await _session.ComposeHdrAsync(new(0, 0, Width, Height), source, token: token);
        }

        private Task<byte[]> ComposeAsync(CancellationToken token)
        {
            return _composeOutput is not null
                ? _composeOutput(token)
                : _session.ComposeAsync(new(0, 0, Width, Height), _source, token);
        }

        private void RefreshState()
        {
            Volatile.Write(ref _width, _session.Selection.Width);
            Volatile.Write(ref _height, _session.Selection.Height);
            Volatile.Write(ref _count, _session.AutomationCount);
            Volatile.Write(ref _canUndo, _session.CanUndo);
            Volatile.Write(ref _canRedo, _session.CanRedo);
        }

        private async Task<object?> ExecuteAsync(Func<CancellationToken, Task<object?>> operation,
            CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await _gate.WaitAsync(linked.Token);
            try
            {
                return await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    ObjectDisposedException.ThrowIf(IsClosed, this);
                    linked.Token.ThrowIfCancellationRequested();
                    if (_available?.Invoke() == false)
                        throw new InvalidOperationException(
                            "The original pinned image or annotation scene is no longer available.");
                    try
                    {
                        return await operation(linked.Token);
                    }
                    finally
                    {
                        RefreshState();
                    }
                });
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
