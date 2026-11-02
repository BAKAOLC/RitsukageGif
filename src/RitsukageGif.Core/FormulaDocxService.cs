using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace RitsukageGif.Core
{
    public sealed class FormulaDocxService : IFormulaDocumentService
    {
        private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace Math = "http://schemas.openxmlformats.org/officeDocument/2006/math";
        private static readonly UTF8Encoding Utf8 = new(false);

        public Task<ReadOnlyMemory<byte>> EncodeDocxAsync(IReadOnlyList<string> mathMl,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(mathMl);
            token.ThrowIfCancellationRequested();
            if (mathMl.Count is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(mathMl));
            var snapshot = mathMl.ToArray();
            long characters = 0;
            foreach (var item in snapshot)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(item);
                characters += item.Length;
                if (characters > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(mathMl));
            }

            return Task.Run<ReadOnlyMemory<byte>>(() => Encode(snapshot, token), token);
        }

        private static byte[] Encode(string[] sources, CancellationToken token)
        {
            var body = new XElement(Word + "body");
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                body.Add(new XElement(Word + "p", new XElement(Math + "oMathPara",
                    new XElement(Math + "oMathParaPr",
                        new XElement(Math + "jc", new XAttribute(Math + "val", "center"))),
                    MathMlToOmml.Convert(source, token))));
            }

            body.Add(new XElement(Word + "sectPr",
                new XElement(Word + "pgSz", new XAttribute(Word + "w", "11906"), new XAttribute(Word + "h", "16838")),
                new XElement(Word + "pgMar", new XAttribute(Word + "top", "1440"),
                    new XAttribute(Word + "right", "1440"),
                    new XAttribute(Word + "bottom", "1440"), new XAttribute(Word + "left", "1440"),
                    new XAttribute(Word + "header", "708"), new XAttribute(Word + "footer", "708"),
                    new XAttribute(Word + "gutter", "0"))));
            var document = new XDocument(new XElement(Word + "document", new XAttribute(XNamespace.Xmlns + "w", Word),
                new XAttribute(XNamespace.Xmlns + "m", Math), body));
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true, Utf8))
            {
                Write(zip, "[Content_Types].xml",
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>",
                    token);
                Write(zip, "_rels/.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>",
                    token);
                Write(zip, "word/document.xml", document.ToString(SaveOptions.DisableFormatting), token);
            }

            token.ThrowIfCancellationRequested();
            return output.ToArray();
        }

        private static void Write(ZipArchive zip, string name, string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            using var writer = new StreamWriter(stream, Utf8);
            writer.Write(text);
        }
    }
}
