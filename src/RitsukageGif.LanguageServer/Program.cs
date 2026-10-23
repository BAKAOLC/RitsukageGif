using RitsukageGif.Contracts;
using RitsukageGif.Scripting;

namespace RitsukageGif.LanguageServer
{
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            UiText.SetLanguage(args.FirstOrDefault() ?? "system");
            await new ScriptLanguageServer().RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
        }
    }
}
