using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static class OcrLanguageSelection
    {
        internal static OcrLanguage Describe(string tag, string? fallback = null)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(tag);
                return new(tag, culture.DisplayName, culture.NativeName);
            }
            catch (CultureNotFoundException)
            {
                return new(tag, fallback ?? tag, fallback ?? tag);
            }
        }

        internal static OcrLanguageChoice Resolve(IReadOnlyList<OcrLanguageChoice> languages, string? tag)
        {
            if (languages.Count == 0) throw new NotSupportedException("No OCR recognition language is installed.");
            var automatic = string.IsNullOrWhiteSpace(tag);
            var requested = string.IsNullOrWhiteSpace(tag) ? CultureInfo.CurrentUICulture.Name : tag;
            var exact = languages.FirstOrDefault(language => language.Language.Tag.Equals(requested,
                StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            try
            {
                var culture = CultureInfo.GetCultureInfo(requested);
                while (culture.Name.Length != 0)
                {
                    exact = languages.FirstOrDefault(language => language.Language.Tag.Equals(culture.Name,
                        StringComparison.OrdinalIgnoreCase));
                    if (exact is not null) return exact;
                    culture = culture.Parent;
                }

                var prefix = requested.Split('-')[0] + "-";
                var regional = languages.FirstOrDefault(language => language.Language.Tag.StartsWith(prefix,
                    StringComparison.OrdinalIgnoreCase));
                if (regional is not null && !requested.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
                    return regional;
            }
            catch (CultureNotFoundException)
            {
            }

            if (automatic) return languages[0];
            throw new NotSupportedException("The requested OCR recognition language is not installed: " + tag);
        }

        internal static IReadOnlyList<OcrLanguageChoice> TesseractLanguages(string output)
        {
            var cultures = CultureInfo.GetCultures(CultureTypes.NeutralCultures);
            var languages = new List<OcrLanguageChoice>();
            foreach (var line in output.Split('\n'))
            {
                var id = line.Trim();
                if (id.Length == 0 || id == "osd" ||
                    id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '/')))
                    continue;
                var tag = id switch
                {
                    "chi_sim" => "zh-Hans",
                    "chi_tra" => "zh-Hant",
                    "chi_sim_vert" => "zh-Hans-vert",
                    "chi_tra_vert" => "zh-Hant-vert",
                    "jpn_vert" => "ja-vert",
                    "kor_vert" => "ko-vert",
                    _ => cultures.FirstOrDefault(culture => culture.ThreeLetterISOLanguageName.Equals(id,
                        StringComparison.OrdinalIgnoreCase))?.Name ?? id.Replace('_', '-').Replace('/', '-'),
                };
                if (languages.Any(language => language.EngineId.Equals(id, StringComparison.Ordinal))) continue;
                languages.Add(new(id, Describe(tag, id)));
            }

            return languages.AsReadOnly();
        }
    }
}
