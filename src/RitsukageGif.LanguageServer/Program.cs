using RitsukageGif.Contracts;
using RitsukageGif.Diagnostics;
using RitsukageGif.Scripting;
using ZLogger;

namespace RitsukageGif.LanguageServer
{
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            var dataDirectory = Environment.GetEnvironmentVariable("RITSUKAGE_DATA_DIRECTORY") ??
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "RitsukageGif");
            LogHub.Initialize(dataDirectory, typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
                "language-server");
            var logger = LogHub.GetLogger("LanguageServer.Program");
            try
            {
                UiText.SetLanguage(args.FirstOrDefault() ?? "system");
                logger.ZLogInformation($"Language server process started.");
                await new ScriptLanguageServer().RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
            }
            catch (Exception error)
            {
                logger.ZLogError(
                    $"Language server process failed. ExceptionType={error.GetType().Name} StackTrace={error.StackTrace}");
                throw;
            }
            finally
            {
                logger.ZLogInformation($"Language server process stopping.");
                await LogHub.ShutdownAsync();
            }
        }
    }
}
