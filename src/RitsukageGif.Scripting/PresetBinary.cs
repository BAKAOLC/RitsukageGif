namespace RitsukageGif.Scripting
{
    public sealed record PresetBinary(string Operator, PresetExpression Left, PresetExpression Right) : PresetExpression
    {
        public override object Evaluate(PresetContext context) => Operator switch
        {
            "and" or "&&" => Left.Boolean(context) && Right.Boolean(context),
            "or" or "||" => Left.Boolean(context) || Right.Boolean(context),
            "+" => Left.Evaluate(context) is string text
                ? text + Right.Evaluate(context)
                : Left.Number(context) + Right.Number(context),
            "-" => Left.Number(context) - Right.Number(context),
            "*" => Left.Number(context) * Right.Number(context),
            "/" => Right.Number(context) is var divisor && divisor != 0
                ? Left.Number(context) / divisor
                : throw new InvalidOperationException(UiText.Get("DslDivisionByZero")),
            ">" => Left.Number(context) > Right.Number(context), ">=" => Left.Number(context) >= Right.Number(context),
            "<" => Left.Number(context) < Right.Number(context), "<=" => Left.Number(context) <= Right.Number(context),
            "==" => Equals(Left.Evaluate(context), Right.Evaluate(context)),
            "!=" => !Equals(Left.Evaluate(context), Right.Evaluate(context)),
            "~" => Left.Evaluate(context) is string value && Right.Evaluate(context) is string pattern &&
                   WindowPreset.Matches(value, pattern),
            _ => throw new InvalidOperationException(UiText.Get("DslInvalidExpression"))
        };
    }
}
