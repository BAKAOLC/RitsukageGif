using System.Globalization;
using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Semi.Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class LiveText
    {
        private const string Prefix = "LocalizedText.";

        public static DynamicResourceExtension Resource(string key)
        {
            return new(Prefix + key);
        }

        public static void ApplyLanguage(string language)
        {
            Dispatcher.UIThread.VerifyAccess();
            UiText.SetLanguage(language);
            RefreshResources();
        }

        public static void RefreshResources()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (Application.Current is not { } app) return;
            foreach (var entry in UiText.GetStrings()) app.Resources[Prefix + entry.Key] = entry.Value;
            foreach (var theme in app.Styles.OfType<SemiTheme>())
                theme.Locale = CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
                    ? "zh-CN"
                    : "en-US");
        }
    }
}
