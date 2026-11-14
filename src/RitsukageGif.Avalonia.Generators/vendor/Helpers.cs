using System.Text;
using Microsoft.CodeAnalysis;

namespace Generator
{
    internal static class Helpers
    {
        private static readonly SymbolDisplayFormat s_symbolDisplayFormat =
            SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        public static StringBuilder Pad(this StringBuilder sb, int count)
        {
            return sb.Append(' ', count * 4);
        }

        public static string GetFullyQualifiedName(this ISymbol symbol)
        {
            return symbol.ToDisplayString(s_symbolDisplayFormat);
        }

        public static bool HasFullyQualifiedName(this ISymbol symbol, string name)
        {
            return symbol.ToDisplayString(s_symbolDisplayFormat) == name;
        }

        public static bool HasAttributeWithFullyQualifiedName(this ISymbol symbol, string name)
        {
            var attributes = symbol.GetAttributes();

            foreach (var attribute in attributes)
                if (attribute.AttributeClass?.HasFullyQualifiedName(name) == true)
                    return true;

            return false;
        }
    }
}
