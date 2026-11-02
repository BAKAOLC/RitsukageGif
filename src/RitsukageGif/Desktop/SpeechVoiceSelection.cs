namespace RitsukageGif.Desktop
{
    internal static class SpeechVoiceSelection
    {
        internal static SpeechVoice? Select(SpeechCapabilities capabilities, string? language)
        {
            if (!capabilities.Available) return null;
            if (string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return capabilities.Voices.FirstOrDefault(voice => voice.Id == capabilities.DefaultVoiceId) ??
                       capabilities.Voices.FirstOrDefault();
            var tag = language.Contains('_') ? language.Replace('_', '-') : language;
            foreach (var voice in capabilities.Voices)
                if (Matches(voice, tag, false))
                    return voice;
            foreach (var voice in capabilities.Voices)
                if (Matches(voice, tag, true))
                    return voice;
            return null;
        }

        private static bool Matches(SpeechVoice voice, string language, bool neutral)
        {
            if (TagMatches(voice.Language, language, neutral)) return true;
            foreach (var alternate in voice.Languages ?? [])
                if (TagMatches(alternate, language, neutral))
                    return true;
            return false;
        }

        private static bool TagMatches(string left, string right, bool neutral)
        {
            return neutral
                ? Neutral(left).Equals(Neutral(right), StringComparison.OrdinalIgnoreCase)
                : left.Equals(right, StringComparison.OrdinalIgnoreCase);
        }

        private static ReadOnlySpan<char> Neutral(string tag)
        {
            var index = tag.IndexOf('-');
            return index < 0 ? tag.AsSpan() : tag.AsSpan(0, index);
        }
    }
}
