using System.Text;

namespace RitsukageGif.Core
{
    public static partial class FormulaTextExporter
    {
        private static bool Boundary(string? symbol, bool left, StringBuilder builder, FormulaTextFormat format,
            out string? error)
        {
            error = null;
            if (string.IsNullOrEmpty(symbol))
            {
                if (format == FormulaTextFormat.AsciiMath) builder.Append(left ? "{:" : ":}");
                return true;
            }

            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (format)
            {
                case FormulaTextFormat.Text:
                    builder.Append(symbol);
                    return true;
                case FormulaTextFormat.Typst:
                    builder.Append(symbol switch
                    {
                        "{" => "\\{", "}" => "\\}", "⟨" => "angle.l", "⟩" => "angle.r", _ => symbol,
                    });
                    return true;
            }

            builder.Append(symbol switch
            {
                "⟨" => "(:", "⟩" => ":)", "⌊" => "|__", "⌋" => "__|", "⌈" => "|~", "⌉" => "~|",
                "‖" => "||", _ => symbol,
            });
            return true;
        }

        private static bool Symbol(string symbol, StringBuilder builder, FormulaTextFormat format, out string? error)
        {
            error = null;
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (format)
            {
                case FormulaTextFormat.Text:
                    builder.Append(symbol);
                    return true;
                case FormulaTextFormat.Typst:
                    if (symbol is "#" or "$" or "%" or "&" or ";" or ":" or "_" or "^" or "\\" or "\"" or "'" or "*"
                        or "/")
                        builder.Append('\\').Append(symbol);
                    else if (symbol.Length > 1 && symbol.All(char.IsLetter)) Quote(builder, symbol);
                    else builder.Append(symbol);
                    return true;
            }

            var converted = symbol switch
            {
                "/" => "//", "*" => "**",
                "−" => "-", "×" => "xx", "÷" => "-:", "⋅" or "·" => "*", "±" => "+-", "∓" => "-+",
                "≤" => "<=", "≥" => ">=", "≠" => "!=", "≈" => "~~", "≡" => "-=", "∼" => "~", "≅" => "~=",
                "∑" => "sum", "∏" => "prod", "∫" => "int", "∮" => "oint", "∞" => "oo",
                "∂" => "del", "∇" => "grad", "∈" => "in", "∉" => "!in", "⊂" => "sub", "⊃" => "sup",
                "⊆" => "sube", "⊇" => "supe", "∪" => "uu", "∩" => "nn", "∅" => "O/",
                "∀" => "AA", "∃" => "EE", "¬" => "not", "∧" => "and", "∨" => "or", "⊕" => "o+", "⊗" => "ox",
                "→" => "->", "←" => "<-", "↔" => "<->", "⇒" => "=>", "⇐" => "Leftarrow", "⇔" => "<=>",
                "α" => "alpha", "β" => "beta", "γ" => "gamma", "δ" => "delta", "ε" => "epsilon",
                "ζ" => "zeta", "η" => "eta", "θ" => "theta", "ϑ" => "vartheta", "ι" => "iota", "κ" => "kappa",
                "λ" => "lambda", "μ" => "mu", "ν" => "nu", "ξ" => "xi", "π" => "pi",
                "ρ" => "rho", "σ" => "sigma", "τ" => "tau", "υ" => "upsilon",
                "φ" => "varphi", "ϕ" => "phi", "χ" => "chi", "ψ" => "psi", "ω" => "omega",
                "Γ" => "Gamma", "Δ" => "Delta", "Θ" => "Theta", "Λ" => "Lambda", "Ξ" => "Xi", "Π" => "Pi",
                "Σ" => "Sigma", "Φ" => "Phi", "Ψ" => "Psi", "Ω" => "Omega",
                "…" => "...", "⋯" => "cdots", "⋮" => "vdots", "⋱" => "ddots",
                _ => null,
            };
            if (converted is not null)
            {
                builder.Append(converted);
                return true;
            }

            if (symbol.All(character => char.IsAsciiLetterOrDigit(character) ||
                                        character is '+' or '-' or '=' or '<' or '>' or ',' or '.' or '!' or '|'))
            {
                builder.Append(symbol);
                return true;
            }

            return Fail(out error, "A formula symbol cannot be represented as AsciiMath.");
        }
    }
}
