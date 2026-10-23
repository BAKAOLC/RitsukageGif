using System.Globalization;
using System.Resources;

namespace RitsukageGif.Contracts
{
    public static class UiText
    {
        private static readonly ResourceManager Resources = new("RitsukageGif.Contracts.Resources.Strings",
            typeof(UiText).Assembly);

        public static string Get(string key)
        {
            return Resources.GetString(key, CultureInfo.CurrentUICulture)
                   ?? throw new MissingManifestResourceException($"Missing resource: {key}");
        }

        public static string Format(string key, params object[] values)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), values);
        }

        public static void SetLanguage(string language)
        {
            var name = language switch
            {
                "zh-Hans" => "zh-Hans",
                "en" => "en",
                _ => CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "zh" ? "zh-Hans" : "en"
            };
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
