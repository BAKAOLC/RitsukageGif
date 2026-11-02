using CSharpMath.Rendering.BackEnd;
using SkiaSharp;
using Typography.OpenFont;

namespace RitsukageGif.Core
{
    internal static class FormulaFontFallback
    {
        private static readonly Lock Gate = new();
        private static readonly Dictionary<int, Typeface> Characters = [];

        private static readonly
            Dictionary<(string Family, int Weight, int Width, SKFontStyleSlant Slant, int Index), Typeface> Faces = [];

        internal static bool TryGet(string source, out IReadOnlyList<Typeface> typefaces, out string? error)
        {
            List<Typeface>? required = null;
            typefaces = [];
            error = null;
            foreach (var rune in source.EnumerateRunes())
            {
                if (rune.Value < 128 || Fonts.GlobalTypefaces.Any(font => font.GetGlyphIndex(rune.Value) != 0))
                    continue;
                Typeface? face;
                try
                {
                    lock (Gate)
                        if (!Characters.TryGetValue(rune.Value, out face))
                        {
                            face = Faces.Values.FirstOrDefault(font => font.GetGlyphIndex(rune.Value) != 0)
                                   ?? Load(rune.Value);
                            if (face is not null) Characters.Add(rune.Value, face);
                        }
                }
                catch (Exception failure) when (failure is IOException or NotSupportedException or ArgumentException)
                {
                    error = "A required system font could not be loaded.";
                    return false;
                }

                if (face is null)
                {
                    error = "A required character is not supported by the available fonts.";
                    return false;
                }

                required ??= [];
                if (!required.Contains(face)) required.Add(face);
            }

            typefaces = required ?? [];
            return true;
        }

        private static Typeface? Load(int codepoint)
        {
            using var matched = SKFontManager.Default.MatchCharacter(codepoint);
            if (matched is null) return null;
            using var asset = matched.OpenStream(out var collectionIndex);
            if (asset is null) return null;
            var key = (matched.FamilyName, matched.FontWeight, matched.FontWidth, matched.FontSlant, collectionIndex);
            if (Faces.TryGetValue(key, out var cached)) return cached;
            using var data = SKData.Create(asset);
            using var previewStream = data.AsStream();
            var reader = new OpenFontReader();
            var preview = reader.ReadPreview(previewStream);
            var offset = preview is PreviewFontCollectionInfo collection
                ? collection.Fonts[collectionIndex].ActualStreamOffset
                : 0;
            using var stream = data.AsStream();
            var face = reader.Read(stream, offset);
            if (face is null || face.GetGlyphIndex(codepoint) == 0) return null;
            if (face.IsCffFont) face.UpdateAllCffGlyphBounds();
            Faces.Add(key, face);
            return face;
        }
    }
}
