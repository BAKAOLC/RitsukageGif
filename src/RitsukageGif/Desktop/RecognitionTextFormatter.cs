using System.Text;

namespace RitsukageGif.Desktop
{
    internal static class RecognitionTextFormatter
    {
        internal static string Format(OcrRecognitionResult result, RecognitionPreferences preferences)
        {
            return Format(result.Text, preferences);
        }

        internal static string Format(string text, RecognitionPreferences preferences)
        {
            preferences = preferences.Normalize();
            var output = preferences.Layout == RecognitionTextLayout.MultiLine
                ? text
                : Reflow(text, preferences.Layout);
            if (preferences.Punctuation == RecognitionPunctuation.None) return output;
            var result = new StringBuilder(output.Length);
            foreach (var character in output)
            {
                var converted = character;
                if (preferences.Punctuation == RecognitionPunctuation.FullWidth)
                {
                    if (character is >= '!' and <= '~' && (char.IsPunctuation(character) || char.IsSymbol(character)))
                        converted = (char)(character + 0xFEE0);
                }
                else if (character is >= '\uFF01' and <= '\uFF5E' &&
                         (char.IsPunctuation(character) || char.IsSymbol(character)))
                {
                    converted = (char)(character - 0xFEE0);
                }
                else
                {
                    converted = character switch
                    {
                        '。' => '.', '、' => ',', '“' or '”' => '"', '‘' or '’' => '\'',
                        '【' => '[', '】' => ']', '《' => '<', '》' => '>', _ => character,
                    };
                }

                result.Append(converted);
            }

            return result.ToString();
        }

        private static string Reflow(string text, RecognitionTextLayout layout)
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            var result = new StringBuilder(text.Length);
            var paragraphBreak = false;
            foreach (var original in lines)
            {
                var line = original.Trim();
                if (line.Length == 0)
                {
                    paragraphBreak = true;
                    continue;
                }

                if (result.Length != 0)
                {
                    var previous = result[^1];
                    if (layout == RecognitionTextLayout.Auto && (paragraphBreak ||
                                                                 previous is '.' or '!' or '?' or '。' or '！' or '？'
                                                                     or ':' or '：'))
                        result.AppendLine();
                    else if (!IsCjk(previous) || !IsCjk(line[0])) result.Append(' ');
                }

                result.Append(line);
                paragraphBreak = false;
            }

            return result.ToString();
        }

        private static bool IsCjk(char value)
        {
            return value is >= '\u2E80' and <= '\u9FFF' or >= '\uAC00' and <= '\uD7AF'
                or >= '\uF900' and <= '\uFAFF' or >= '\uFF00' and <= '\uFFEF';
        }
    }
}
