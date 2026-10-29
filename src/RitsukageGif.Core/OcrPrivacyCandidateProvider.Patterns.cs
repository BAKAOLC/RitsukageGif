using System.Globalization;
using System.Text.RegularExpressions;

namespace RitsukageGif.Core
{
    public sealed partial class OcrPrivacyCandidateProvider
    {
        [GeneratedRegex(
            @"[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]*[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]*[A-Z0-9])?)+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
        private static partial Regex EmailPattern();

        [GeneratedRegex("(?<![0-9])[1-9][0-9]{16}[0-9Xx](?![0-9])", RegexOptions.CultureInvariant, 100)]
        private static partial Regex IdentityPattern();

        [GeneratedRegex("(?<![0-9])[0-9](?:[ -]?[0-9]){12,18}(?![0-9])", RegexOptions.CultureInvariant, 100)]
        private static partial Regex CardPattern();

        [GeneratedRegex(
            @"(?<![0-9])(?:\+[1-9](?:[ .-]?[0-9]){7,14}|(?:86[ -]?)?1[3-9][0-9]{9}|0[0-9]{2,3}[ -][0-9]{7,8}|[2-9][0-9]{2}[- .][0-9]{3}[- .][0-9]{4})(?![0-9])",
            RegexOptions.CultureInvariant, 100)]
        private static partial Regex PhonePattern();

        private static bool ValidCard(ReadOnlySpan<char> text)
        {
            Span<char> digits = stackalloc char[19];
            var count = 0;
            foreach (var character in text)
                if (char.IsAsciiDigit(character))
                {
                    if (count >= digits.Length) return false;
                    digits[count++] = character;
                }

            if (count < 13 || digits[..count].IndexOfAnyExcept(digits[0]) < 0) return false;
            var sum = 0;
            var twice = false;
            for (var index = count - 1; index >= 0; index--)
            {
                var value = digits[index] - '0';
                if (twice)
                {
                    value *= 2;
                    if (value > 9) value -= 9;
                }

                sum += value;
                twice = !twice;
            }

            return sum % 10 == 0;
        }

        private static bool ValidIdentity(ReadOnlySpan<char> text)
        {
            if (text.Length != 18 || !DateOnly.TryParseExact(text.Slice(6, 8), "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var birth) || birth.Year < 1800 ||
                birth > DateOnly.FromDateTime(DateTime.Today))
                return false;
            ReadOnlySpan<int> weights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
            const string checksums = "10X98765432";
            var sum = 0;
            for (var index = 0; index < 17; index++) sum += (text[index] - '0') * weights[index];
            return char.ToUpperInvariant(text[17]) == checksums[sum % 11];
        }
    }
}
