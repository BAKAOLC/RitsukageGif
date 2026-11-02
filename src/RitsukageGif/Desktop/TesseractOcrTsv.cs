using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static class TesseractOcrTsv
    {
        internal static OcrRecognitionResult Parse(string tsv, string text, string language, int width, int height)
        {
            using var reader = new StringReader(tsv);
            if (reader.ReadLine()?.TrimEnd('\r') !=
                "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext")
                throw new InvalidDataException("The OCR process returned an invalid TSV header.");
            var lines = new Dictionary<(int Page, int Block, int Paragraph, int Line), TesseractOcrLine>();
            while (reader.ReadLine() is { } row)
            {
                if (row.Length == 0) continue;
                var fields = row.Split('\t', 12);
                if (fields.Length != 12) throw new InvalidDataException("The OCR process returned an invalid TSV row.");
                var level = Number(fields[0]);
                if (level is not (4 or 5)) continue;
                var key = (Number(fields[1]), Number(fields[2]), Number(fields[3]), Number(fields[4]));
                var left = Number(fields[6]);
                var top = Number(fields[7]);
                var right = Math.Clamp((long)left + Number(fields[8]), 0, width);
                var bottom = Math.Clamp((long)top + Number(fields[9]), 0, height);
                left = Math.Clamp(left, 0, width);
                top = Math.Clamp(top, 0, height);
                if (right <= left || bottom <= top) continue;
                var bounds = new OcrBounds(left, top, right - left, bottom - top);
                if (level == 4)
                {
                    if (!lines.TryAdd(key, new(bounds)))
                        throw new InvalidDataException("The OCR process returned a duplicate text line.");
                }
                else if (!string.IsNullOrWhiteSpace(fields[11]))
                {
                    if (!lines.TryGetValue(key, out var line))
                        throw new InvalidDataException("The OCR word has no containing text line.");
                    line.Words.Add(new(fields[11], bounds));
                }
            }

            text = text.TrimEnd('\r', '\n', '\f');
            var plainLines = text.Split('\n').Select(line => line.TrimEnd('\r'))
                .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var detected = lines.Values.Where(line => line.Words.Count != 0).ToArray();
            var output = new OcrLine[detected.Length];
            for (var index = 0; index < detected.Length; index++)
            {
                var line = detected[index];
                var lineText = plainLines.Length == detected.Length
                    ? plainLines[index]
                    : string.Join(' ', line.Words.Select(word => word.Text));
                output[index] = new(lineText, line.Bounds, line.Words.AsReadOnly());
            }

            return new(text, language, width, height, null, Array.AsReadOnly(output));
        }

        private static int Number(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException("The OCR process returned a nonnumeric TSV coordinate.");
            return value;
        }
    }
}
