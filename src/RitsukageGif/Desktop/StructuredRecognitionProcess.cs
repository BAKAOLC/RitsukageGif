using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed class StructuredRecognitionProcess : ITableRecognitionService, IFormulaRecognitionService,
        IFormulaOutputService,
        IAsyncDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.StructuredRecognition");
        private readonly SemaphoreSlim _gate = new(1);
        private readonly StructuredRecognitionModelPaths _paths;
        private readonly Func<ProcessStartInfo> _startInfo;
        private bool _disposed;
        private Task<string>? _error;
        private Process? _process;

        private StructuredRecognitionProcess(StructuredRecognitionModelPaths paths,
            Func<ProcessStartInfo>? startInfoFactory = null)
        {
            _paths = paths;
            _startInfo = startInfoFactory ?? Program.CreateSelfStartInfo;
        }

        internal static StructuredRecognitionProcess Shared { get; } = new(StructuredRecognitionModelPaths.InDirectory(
            Path.Combine(ApplicationStorage.DataDirectory, "components", "recognition")));

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _disposed = true;
                await StopWorkerAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<FormulaRenderResult> RenderAsync(IReadOnlyList<string> sources, FormulaImageFormat format,
            float fontSize = 28, uint textColor = 0xff000000, CancellationToken token = default)
        {
            return format switch
            {
                FormulaImageFormat.Png => RenderManyPngAsync(sources, fontSize, textColor, token),
                FormulaImageFormat.Svg => RenderManySvgAsync(sources, fontSize, textColor, token),
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
        }

        Task<FormulaRenderResult> IFormulaOutputService.ConvertAsync(IReadOnlyList<string> sources,
            FormulaTextFormat format, CancellationToken token)
        {
            return ConvertManyAsync(sources, format, token);
        }

        async Task<FormulaRecognitionResult> IFormulaRecognitionService.RecognizeAsync(ReadOnlyMemory<byte> encoded,
            CancellationToken token)
        {
            return (await RequestAsync(encoded, StructuredRecognitionKind.Formula,
                           StructuredRecognitionRenderFormat.None, 28, 0xff000000, token)
                       .ConfigureAwait(false)).Formula
                   ?? throw new InvalidDataException("The recognition worker returned no formula.");
        }

        async Task<RecognitionProviderCapabilities> IFormulaRecognitionService.GetCapabilitiesAsync(
            CancellationToken token)
        {
            await using var service = new OnnxFormulaRecognitionService(_paths);
            return await service.GetCapabilitiesAsync(token).ConfigureAwait(false);
        }

        async Task<TableRecognitionResult> ITableRecognitionService.RecognizeAsync(ReadOnlyMemory<byte> encoded,
            CancellationToken token)
        {
            return (await RequestAsync(encoded, StructuredRecognitionKind.Table, StructuredRecognitionRenderFormat.None,
                           28, 0xff000000, token)
                       .ConfigureAwait(false)).Table
                   ?? throw new InvalidDataException("The recognition worker returned no table.");
        }

        async Task<RecognitionProviderCapabilities> ITableRecognitionService.GetCapabilitiesAsync(
            CancellationToken token)
        {
            await using var service = new OnnxTableRecognitionService(_paths);
            return await service.GetCapabilitiesAsync(token).ConfigureAwait(false);
        }

        internal async Task<FormulaRenderResult> RenderPngAsync(string latex, float fontSize = 28,
            uint textColor = 0xff000000, CancellationToken token = default)
        {
            return (await RequestAsync(Encoding.UTF8.GetBytes(latex), StructuredRecognitionKind.Formula,
                       StructuredRecognitionRenderFormat.Png,
                       fontSize, textColor, token).ConfigureAwait(false)).Rendered
                   ?? throw new InvalidDataException("The rendering worker returned no result.");
        }

        internal async Task<FormulaRenderResult> ConvertAsync(string latex, FormulaTextFormat format,
            CancellationToken token = default)
        {
            var target = format switch
            {
                FormulaTextFormat.Latex => StructuredRecognitionRenderFormat.Latex,
                FormulaTextFormat.Text => StructuredRecognitionRenderFormat.Text,
                FormulaTextFormat.MathMl => StructuredRecognitionRenderFormat.MathMl,
                FormulaTextFormat.AsciiMath => StructuredRecognitionRenderFormat.AsciiMath,
                FormulaTextFormat.Typst => StructuredRecognitionRenderFormat.Typst,
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
            return (await RequestAsync(Encoding.UTF8.GetBytes(latex), StructuredRecognitionKind.Formula, target, 28,
                           0xff000000, token)
                       .ConfigureAwait(false)).Rendered
                   ?? throw new InvalidDataException("The conversion worker returned no result.");
        }

        internal Task<FormulaRenderResult> RenderManyPngAsync(IReadOnlyList<string> sources, float fontSize = 28,
            uint textColor = 0xff000000, CancellationToken token = default)
        {
            return RequestManyAsync(sources, StructuredRecognitionRenderFormat.Png, fontSize, textColor, token);
        }

        internal Task<FormulaRenderResult> RenderManySvgAsync(IReadOnlyList<string> sources, float fontSize = 28,
            uint textColor = 0xff000000, CancellationToken token = default)
        {
            return RequestManyAsync(sources, StructuredRecognitionRenderFormat.Svg, fontSize, textColor, token);
        }

        internal Task<FormulaRenderResult> ConvertManyAsync(IReadOnlyList<string> sources, FormulaTextFormat format,
            CancellationToken token = default)
        {
            var target = format switch
            {
                FormulaTextFormat.Latex => StructuredRecognitionRenderFormat.Latex,
                FormulaTextFormat.Text => StructuredRecognitionRenderFormat.Text,
                FormulaTextFormat.MathMl => StructuredRecognitionRenderFormat.MathMl,
                FormulaTextFormat.AsciiMath => StructuredRecognitionRenderFormat.AsciiMath,
                FormulaTextFormat.Typst => StructuredRecognitionRenderFormat.Typst,
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
            return RequestManyAsync(sources, target, 28, 0xff000000, token);
        }

        private async Task<FormulaRenderResult> RequestManyAsync(IReadOnlyList<string> sources,
            StructuredRecognitionRenderFormat format, float fontSize, uint color, CancellationToken token)
        {
            var snapshot = sources.ToArray();
            var encoded =
                JsonSerializer.SerializeToUtf8Bytes(snapshot, StructuredRecognitionJsonContext.Default.StringArray);
            return (await RequestAsync(encoded, StructuredRecognitionKind.Formula, format, fontSize, color, token, true)
                       .ConfigureAwait(false))
                   .Rendered
                   ?? throw new InvalidDataException("The formula worker returned no output.");
        }

        internal async Task<OcrCapabilities> GetTextCapabilitiesAsync(CancellationToken token)
        {
            await using var service = new OnnxTextRecognitionService(_paths);
            return await service.GetCapabilitiesAsync(token).ConfigureAwait(false);
        }

        internal async Task<OcrRecognitionResult> RecognizeTextAsync(ReadOnlyMemory<byte> pixels,
            int width, int height, string? languageTag, CancellationToken token)
        {
            return (await RequestAsync(pixels, StructuredRecognitionKind.Text, StructuredRecognitionRenderFormat.None,
                           28, 0xff000000, token, width: width, height: height, languageTag: languageTag)
                       .ConfigureAwait(false)).Text
                   ?? throw new InvalidDataException("The recognition worker returned no text.");
        }

        private async Task<StructuredRecognitionReply> RequestAsync(ReadOnlyMemory<byte> encoded,
            StructuredRecognitionKind kind,
            StructuredRecognitionRenderFormat renderFormat, float fontSize, uint textColor, CancellationToken token,
            bool sourcesArray = false, int width = 0, int height = 0, string? languageTag = null)
        {
            if (encoded.Length is <= 0 or > 64 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(encoded));
            await _gate.WaitAsync(token).ConfigureAwait(false);
            OwnedWorkerDirectory? directory = null;
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                directory = OwnedWorkerDirectory.Create("recognition");
                var input = Path.Combine(directory.DirectoryPath, "input.bin");
                await using (var stream = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await stream.WriteAsync(encoded, token).ConfigureAwait(false);
                EnsureWorker();
                var request = JsonSerializer.Serialize(
                    new(kind, input, renderFormat, fontSize, textColor, sourcesArray, width, height, languageTag),
                    StructuredRecognitionJsonContext.Default.StructuredRecognitionRequest);
                await _process!.StandardInput.WriteLineAsync(request.AsMemory(), token).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(token).ConfigureAwait(false);
                var line = await _process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                {
                    await _process.WaitForExitAsync(token).ConfigureAwait(false);
                    var detail = await _error!.ConfigureAwait(false);
                    Logger.ZLogWarning(
                        $"Recognition worker exited unexpectedly. ExitCode={_process.ExitCode} Detail={detail}");
                    throw new InvalidOperationException(
                        "The recognition worker stopped unexpectedly. See the application logs.");
                }

                var reply = JsonSerializer.Deserialize(line,
                                StructuredRecognitionJsonContext.Default.StructuredRecognitionReply)
                            ?? throw new InvalidDataException("The recognition worker returned an empty response.");
                if (reply.Error is { } error) throw new InvalidOperationException(error);
                return reply;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await StopWorkerAsync().ConfigureAwait(false);
                Logger.ZLogInformation($"Recognition cancelled; the isolated worker was stopped.");
                throw;
            }
            catch (Exception error)
            {
                await StopWorkerAsync().ConfigureAwait(false);
                Logger.ZLogWarning(error, $"Isolated structured recognition failed.");
                throw;
            }
            finally
            {
                directory?.Dispose();
                _gate.Release();
            }
        }

        private void EnsureWorker()
        {
            if (_process is { HasExited: false }) return;
            _process?.Dispose();
            var start = _startInfo();
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.ArgumentList.Add("--structured-recognition-worker");
            start.ArgumentList.Add(_paths.Directory);
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            _process = Process.Start(start) ??
                       throw new InvalidOperationException("Cannot start the recognition worker.");
            _error = _process.StandardError.ReadToEndAsync();
            Logger.ZLogDebug($"Recognition worker started. WorkerProcessId={_process.Id}");
        }

        private async Task StopWorkerAsync()
        {
            if (_process is not { } process) return;
            _process = null;
            try
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync().ConfigureAwait(false);
                if (_error is { } error) await error.ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
                _error = null;
            }
        }

        internal static int RunWorker(string directory, int parentProcessId)
        {
            DiagnosticLog.Initialize("structured-recognition");
            try
            {
                using var parent = Process.GetProcessById(parentProcessId);
                _ = parent.WaitForExitAsync().ContinueWith(_ => Environment.Exit(0), TaskScheduler.Default);
                RunWorkerAsync(directory).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Recognition worker failed.");
                Console.Error.WriteLine(error.Message);
                return 1;
            }
            finally
            {
                DiagnosticLog.Shutdown();
            }
        }

        private static async Task RunWorkerAsync(string directory)
        {
            var paths = StructuredRecognitionModelPaths.InDirectory(directory);
            await using var tables = new OnnxTableRecognitionService(paths);
            await using var formulas = new OnnxFormulaRecognitionService(paths);
            await using var text = new OnnxTextRecognitionService(paths);
            using var input = new StreamReader(Console.OpenStandardInput());
            await using var output = new StreamWriter(Console.OpenStandardOutput());
            output.AutoFlush = true;
            while (await input.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                StructuredRecognitionReply reply;
                try
                {
                    var request = JsonSerializer.Deserialize(line,
                                      StructuredRecognitionJsonContext.Default.StructuredRecognitionRequest)
                                  ?? throw new InvalidDataException("Empty recognition request.");
                    var started = Stopwatch.GetTimestamp();
                    Logger.ZLogDebug(
                        $"Recognition request started. Kind={request.Kind.ToString()} Width={request.PixelWidth} Height={request.PixelHeight}");
                    if (request.RenderFormat != StructuredRecognitionRenderFormat.None)
                    {
                        reply = await RenderFormulaAsync(request).ConfigureAwait(false);
                    }
                    else
                    {
                        var image = await File.ReadAllBytesAsync(request.ImagePath).ConfigureAwait(false);
                        reply = request.Kind switch
                        {
                            StructuredRecognitionKind.Formula => new(
                                Formula: await formulas.RecognizeAsync(image).ConfigureAwait(false)),
                            StructuredRecognitionKind.Table => new(await tables.RecognizeAsync(image)
                                .ConfigureAwait(false)),
                            StructuredRecognitionKind.Text => new(Text: await text.RecognizeBgraAsync(image,
                                request.PixelWidth, request.PixelHeight, checked(request.PixelWidth * 4),
                                request.LanguageTag).ConfigureAwait(false)),
                            _ => throw new InvalidDataException("The recognition operation is not supported."),
                        };
                    }

                    Logger.ZLogDebug(
                        $"Recognition request completed. Kind={request.Kind.ToString()} ElapsedMilliseconds={Stopwatch.GetElapsedTime(started).TotalMilliseconds} TextLines={reply.Text?.Lines.Count ?? 0}");
                }
                catch (Exception error)
                {
                    Logger.ZLogWarning(error, $"Recognition request failed.");
                    reply = new(Error: error.Message);
                }

                await output.WriteLineAsync(JsonSerializer.Serialize(reply,
                    StructuredRecognitionJsonContext.Default.StructuredRecognitionReply)).ConfigureAwait(false);
            }
        }

        private static async Task<StructuredRecognitionReply> RenderFormulaAsync(StructuredRecognitionRequest request)
        {
            try
            {
                var source = await File.ReadAllTextAsync(request.ImagePath).ConfigureAwait(false);
                var sources = request.SourcesArray
                    ? JsonSerializer.Deserialize(source, StructuredRecognitionJsonContext.Default.StringArray)
                      ?? throw new InvalidDataException("The formula sources are missing.")
                    : [source];
                Logger.ZLogDebug(
                    $"Formula rendering started. Format={request.RenderFormat.ToString()} Formulas={sources.Length} SourceCharacters={source.Length}");
                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (request.RenderFormat == StructuredRecognitionRenderFormat.Png)
                {
                    var valid = FormulaImageRenderer.TryRenderManyPng(sources, out var png, out var error,
                        request.FontSize,
                        request.TextColor);
                    Logger.ZLogDebug(
                        $"Formula rendering completed. Format=Png Valid={valid} OutputBytes={png?.Length ?? 0}");
                    return new(Rendered: new(png, ValidationError: error));
                }

                if (request.RenderFormat == StructuredRecognitionRenderFormat.Svg)
                {
                    var valid = FormulaImageRenderer.TryRenderManySvg(sources, out var svg, out var error,
                        request.FontSize,
                        request.TextColor);
                    Logger.ZLogDebug(
                        $"Formula rendering completed. Format=Svg Valid={valid} OutputCharacters={svg?.Length ?? 0}");
                    return new(Rendered: new(Svg: svg, ValidationError: error));
                }

                var format = request.RenderFormat switch
                {
                    StructuredRecognitionRenderFormat.Latex => FormulaTextFormat.Latex,
                    StructuredRecognitionRenderFormat.Text => FormulaTextFormat.Text,
                    StructuredRecognitionRenderFormat.MathMl => FormulaTextFormat.MathMl,
                    StructuredRecognitionRenderFormat.AsciiMath => FormulaTextFormat.AsciiMath,
                    StructuredRecognitionRenderFormat.Typst => FormulaTextFormat.Typst,
                    _ => (FormulaTextFormat?)null,
                };
                if (format is { } textFormat)
                {
                    var valid = FormulaTextExporter.TryExportMany(sources, textFormat, out var text, out var error);
                    Logger.ZLogDebug(
                        $"Formula conversion completed. Format={textFormat.ToString()} Valid={valid} OutputCharacters={text?.Length ?? 0}");
                    return new(Rendered: new(Text: text, ValidationError: error));
                }

                return new(Error: "The formula rendering format is not supported.");
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(
                    $"Formula rendering failed. ErrorType={error.GetType().Name} HResult={error.HResult} StackTrace={error.StackTrace}");
                return new(Error: "The formula renderer failed. See the application logs.");
            }
        }
    }
}
