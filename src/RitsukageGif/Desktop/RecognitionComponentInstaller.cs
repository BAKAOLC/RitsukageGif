using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static class RecognitionComponentInstaller
    {
        private static readonly HttpClient Client = CreateClient();
        private static readonly SemaphoreSlim Gate = new(1);
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.RecognitionComponents");

        private static HttpClient CreateClient()
        {
            var client = ApplicationHttp.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(20);
            return client;
        }

        internal static async Task InstallAsync(bool formula, IProgress<string>? progress, CancellationToken token)
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            var directory = Path.Combine(ApplicationStorage.DataDirectory, "components", "recognition");
            var temporary = Path.Combine(Path.GetTempPath(), "RitsukageGif-models-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temporary);
                Directory.CreateDirectory(directory);
                var runtimeName = OperatingSystem.IsWindows() ? "onnxruntime.dll" :
                    OperatingSystem.IsMacOS() ? "libonnxruntime.dylib" : "libonnxruntime.so";
                if (!File.Exists(Path.Combine(directory, runtimeName)))
                {
                    var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
                    // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
                    var architecture = RuntimeInformation.ProcessArchitecture switch
                    {
                        Architecture.X64 => OperatingSystem.IsMacOS() ? "x86_64" : "x64",
                        Architecture.Arm64 => OperatingSystem.IsLinux() ? "aarch64" : "arm64",
                        _ => throw new PlatformNotSupportedException(
                            "The recognition component does not support this architecture."),
                    };
                    var extension = OperatingSystem.IsWindows() ? ".zip" : ".tgz";
                    var asset = $"onnxruntime-{platform}-{architecture}-1.23.2{extension}";
                    var archive = Path.Combine(temporary, asset);
                    await DownloadAsync("https://github.com/microsoft/onnxruntime/releases/download/v1.23.2/" + asset,
                        archive, progress, token).ConfigureAwait(false);
                    await ExtractRuntimeAsync(archive, temporary, runtimeName, token).ConfigureAwait(false);
                    if (!File.Exists(Path.Combine(temporary, runtimeName)))
                        throw new InvalidDataException("The runtime archive contains no compatible runtime.");
                }

                var models = formula
                    ? new[]
                    {
                        "encoder.onnx", "decoder.onnx", "image_resizer.onnx", "tokenizer.json", "pp_doclayout_s.onnx",
                    }
                    : ["slanet-plus.onnx", "PP-OCRv6_rec_tiny.onnx"];
                foreach (var model in models)
                {
                    if (File.Exists(Path.Combine(directory, model))) continue;
                    var url = model == "pp_doclayout_s.onnx"
                        ? "https://www.modelscope.cn/models/RapidAI/RapidDoc/resolve/v1.0.0/layout/PP-DocLayout-S/pp_doclayout_s.onnx"
                        : formula
                            ? "https://github.com/RapidAI/RapidLaTeXOCR/releases/download/v0.0.0/" + model
                            : model == "slanet-plus.onnx"
                                ? "https://www.modelscope.cn/models/RapidAI/RapidTable/resolve/v2.0.0/slanet-plus.onnx"
                                : "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv6/rec/PP-OCRv6_rec_tiny.onnx";
                    await DownloadAsync(url, Path.Combine(temporary, model), progress, token).ConfigureAwait(false);
                }

                await DownloadAsync(formula
                        ? "https://raw.githubusercontent.com/RapidAI/RapidLaTeXOCR/main/LICENSE"
                        : "https://raw.githubusercontent.com/RapidAI/RapidTable/main/LICENSE",
                    Path.Combine(temporary, formula ? "RapidLaTeXOCR-LICENSE.txt" : "RapidTable-LICENSE.txt"), progress,
                    token).ConfigureAwait(false);
                if (formula)
                    await DownloadAsync("https://raw.githubusercontent.com/RapidAI/RapidDoc/main/LICENSE",
                        Path.Combine(temporary, "RapidDoc-LICENSE.txt"), progress, token).ConfigureAwait(false);
                else
                    await DownloadAsync(
                        "https://raw.githubusercontent.com/RapidAI/RapidOCR/main/python/MODEL_LICENSES.md",
                        Path.Combine(temporary, "RapidOCR-MODEL-LICENSES.md"), progress, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                foreach (var file in Directory.EnumerateFiles(temporary))
                {
                    if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) continue;
                    File.Move(file, Path.Combine(directory, Path.GetFileName(file)), true);
                }

                Logger.ZLogInformation($"Offline recognition component installation completed. Formula={formula}");
            }
            finally
            {
                try
                {
                    Directory.Delete(temporary, true);
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Logger.ZLogWarning(error, $"Recognition component staging cleanup was deferred.");
                }

                Gate.Release();
            }
        }

        private static async Task DownloadAsync(string url, string target, IProgress<string>? progress,
            CancellationToken token)
        {
            progress?.Report(Path.GetFileName(target));
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[81920];
            long received = 0;
            var reported = Environment.TickCount64;
            while (await input.ReadAsync(buffer, token).ConfigureAwait(false) is var count && count != 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                received += count;
                if (Environment.TickCount64 - reported < 250) continue;
                reported = Environment.TickCount64;
                progress?.Report(Path.GetFileName(target) + "  " +
                                 (received / 1048576d).ToString("F1", CultureInfo.InvariantCulture) + " MiB");
            }
        }

        private static async Task ExtractRuntimeAsync(string archive, string directory, string runtimeName,
            CancellationToken token)
        {
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await using var zip = await ZipFile.OpenReadAsync(archive, token).ConfigureAwait(false);
                foreach (var entry in zip.Entries)
                {
                    var name = Path.GetFileName(entry.FullName);
                    if (!RuntimeAsset(name, runtimeName)) continue;
                    await using var input = await entry.OpenAsync(token).ConfigureAwait(false);
                    await CopyAsync(input, name);
                }
            }
            else
            {
                await using var file = File.OpenRead(archive);
                await using var compressed = new GZipStream(file, CompressionMode.Decompress);
                await using var tar = new TarReader(compressed);
                while (await tar.GetNextEntryAsync(cancellationToken: token).ConfigureAwait(false) is { } entry)
                {
                    var name = Path.GetFileName(entry.Name);
                    if (!RuntimeAsset(name, runtimeName) || entry.DataStream is not { } input) continue;
                    await CopyAsync(input, name);
                }
            }

            return;

            async Task CopyAsync(Stream input, string name)
            {
                if (name.StartsWith("libonnxruntime.", StringComparison.Ordinal) &&
                    name != "libonnxruntime_providers_shared.so") name = runtimeName;
                await using var output = File.Create(Path.Combine(directory, name));
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            }
        }

        private static bool RuntimeAsset(string name, string runtimeName)
        {
            return name == runtimeName || name is "onnxruntime_providers_shared.dll"
                       or "libonnxruntime_providers_shared.so" or "LICENSE" or "ThirdPartyNotices.txt" ||
                   name.StartsWith(runtimeName + ".", StringComparison.Ordinal) ||
                   (runtimeName.EndsWith(".dylib", StringComparison.Ordinal) &&
                    name.StartsWith("libonnxruntime.", StringComparison.Ordinal) &&
                    name.EndsWith(".dylib", StringComparison.Ordinal));
        }
    }
}
