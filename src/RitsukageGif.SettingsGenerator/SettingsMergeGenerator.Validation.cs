using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RitsukageGif.SettingsGenerator
{
    public sealed partial class SettingsMergeGenerator
    {
        private static bool ValidateModel(SourceProductionContext output, INamedTypeSymbol record,
            INamedTypeSymbol? owner)
        {
            string? error = null;
            if (!record.IsRecord || !record.IsSealed || record.TypeKind != TypeKind.Class || record.Arity != 0 ||
                record.ContainingType is not null || record.BaseType?.SpecialType != SpecialType.System_Object)
                error =
                    "Settings merge models must be sealed, non-generic, top-level records without a record base type";
            else if (owner is null || owner.Arity != 0 || owner.ContainingType is not null ||
                     owner.TypeKind != TypeKind.Class ||
                     owner.DeclaringSyntaxReferences.Length == 0 ||
                     owner.DeclaringSyntaxReferences.Any(static declaration =>
                         declaration.GetSyntax() is not TypeDeclarationSyntax syntax ||
                         !syntax.Modifiers.Any(SyntaxKind.PartialKeyword)))
                error = "The settings merge owner must be a non-generic, top-level partial class or record";
            if (error is null) return true;
            output.ReportDiagnostic(Diagnostic.Create(UnsupportedModel, record.Locations.FirstOrDefault(), error));
            return false;
        }

        private static bool IsIgnored(IPropertySymbol property)
        {
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() !=
                    "System.Text.Json.Serialization.JsonIgnoreAttribute") continue;
                var condition = attribute.NamedArguments.FirstOrDefault(static argument => argument.Key == "Condition");
                return condition.Key is null || condition.Value.Value is 1;
            }

            return false;
        }

        private static bool IsSupported(ITypeSymbol type)
        {
            while (true)
            {
                if (type.SpecialType == SpecialType.System_String) return true;
                if (!type.IsValueType || type is not INamedTypeSymbol named) return false;
                if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    type = named.TypeArguments[0];
                    continue;
                }

                if (named.AllInterfaces.Any(static implemented =>
                        implemented.SpecialType is SpecialType.System_Collections_IEnumerable or
                            SpecialType.System_Collections_Generic_IEnumerable_T)) return false;
                return named.TypeKind == TypeKind.Enum || named.AllInterfaces.Any(implemented =>
                    implemented.OriginalDefinition.ToDisplayString() == "System.IEquatable<T>" &&
                    SymbolEqualityComparer.Default.Equals(implemented.TypeArguments[0], named));
            }
        }
    }
}
