using System.Text.RegularExpressions;

namespace RitsukageGif.IconGenerator
{
    internal static class SvgPathValidator
    {
        private static readonly Regex Tokens = new(@"[AaCcHhLlMmQqSsTtVvZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?",
            RegexOptions.CultureInvariant);

        internal static void Validate(string? data)
        {
            if (string.IsNullOrWhiteSpace(data))
                throw new InvalidDataException("A nonempty path d attribute is required.");
            var matches = Tokens.Matches(data!);
            var index = 0;
            var command = '\0';
            var count = 0;
            var expected = 0;
            foreach (Match token in matches)
            {
                if (data!.Substring(index, token.Index - index)
                    .Any(static character => !char.IsWhiteSpace(character) && character != ','))
                    throw new InvalidDataException("Path data contains unsupported tokens.");
                index = token.Index + token.Length;
                if (char.IsLetter(token.Value[0]))
                {
                    if (command != '\0' && (expected == 0 ? count != 0 : count == 0 || count % expected != 0))
                        throw new InvalidDataException("Path command has an invalid parameter count.");
                    if (command == '\0' && char.ToUpperInvariant(token.Value[0]) != 'M')
                        throw new InvalidDataException("Path data must begin with moveto.");
                    command = char.ToUpperInvariant(token.Value[0]);
                    expected = command switch
                    {
                        'Z' => 0, 'H' or 'V' => 1, 'M' or 'L' or 'T' => 2, 'S' or 'Q' => 4, 'C' => 6, 'A' => 7,
                        _ => 0,
                    };
                    count = 0;
                }
                else
                {
                    if (command == '\0' || expected == 0)
                        throw new InvalidDataException("Path numbers require a drawing command.");
                    var value = SvgIconGenerator.ParseNumber(token.Value);
                    switch (command)
                    {
                        case 'A' when count % 7 is 3 or 4 && value is not (0 or 1):
                            throw new InvalidDataException("Arc flags must be 0 or 1.");
                        case 'A' when count % 7 is 0 or 1 && value < 0:
                            throw new InvalidDataException("Arc radii cannot be negative.");
                    }

                    count++;
                }
            }

            if (command == '\0' || data!.Substring(index).Any(static character => !char.IsWhiteSpace(character)) ||
                (expected != 0 && (count == 0 || count % expected != 0)))
                throw new InvalidDataException("Path data is incomplete.");
        }
    }
}
