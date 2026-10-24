using System.Globalization;
using System.Resources;

namespace RitsukageGif.Contracts
{
    public static class UiText
    {
        private static CultureInfo _culture = CultureInfo.CurrentUICulture;
        public static event Action? LanguageChanged;

        private static readonly ResourceManager Resources = new("RitsukageGif.Contracts.Resources.Strings",
            typeof(UiText).Assembly);

        public static string Get(string key)
        {
            return Resources.GetString(key, Volatile.Read(ref _culture))
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
            var previous = Interlocked.Exchange(ref _culture, culture);
            if (previous.Name != culture.Name) LanguageChanged?.Invoke();
        }

        public static IReadOnlyDictionary<string, string> GetStrings()
        {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            var defaults = Resources.GetResourceSet(CultureInfo.InvariantCulture, true, true)
                           ?? throw new MissingManifestResourceException();
            foreach (System.Collections.DictionaryEntry entry in defaults)
                if (entry.Key is string key)
                    strings.Add(key, Get(key));
            return strings;
        }
    }
}
