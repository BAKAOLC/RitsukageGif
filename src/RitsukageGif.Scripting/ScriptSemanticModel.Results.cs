using System.Text.RegularExpressions;

namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptSemanticModel
    {
        [GeneratedRegex(@"\$(\d+)")]
        private static partial Regex GenericParameterRegex();

        private static string DisplayReturnType(string type)
        {
            return GenericParameterRegex().Replace(type, match => "T" + match.Groups[1].Value);
        }

        private void CheckPropagation(ScriptNode node, ScriptNode? function, Scope scope)
        {
            var actual = Infer(node.Children[0], scope).TypeName;
            if (actual == "any") return;
            var generic = ScriptBindingRegistry.TryGetGenericType(actual, out var family, out var arguments);
            family = generic ? family : actual;
            if (_bindings.GetGenericTypeBinding(family)?.OutcomeValueParameter is not { } valueParameter)
            {
                Diagnostic(node, UiText.Get("ScriptTypeError") + " (" + actual + " ?)");
                return;
            }

            if (function?.TypeName is not { } expected) return;
            var expectedGeneric =
                ScriptBindingRegistry.TryGetGenericType(expected, out var expectedFamily, out var expectedArguments);
            expectedFamily = expectedGeneric ? expectedFamily : expected;
            var compatible = family == expectedFamily;
            if (compatible && generic && expectedGeneric)
                for (var i = 0; i < arguments.Length; i++)
                    if (i != valueParameter && (i >= expectedArguments.Length ||
                                                !_bindings.IsAssignable(arguments[i], expectedArguments[i])))
                        compatible = false;
            if (!compatible)
                Diagnostic(node, UiText.Get("ScriptTypeError") + " (" + actual + " → " + expected + ")");
        }
    }
}
