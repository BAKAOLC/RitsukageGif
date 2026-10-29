namespace RitsukageGif.Scripting
{
    public sealed record PresetUnary(string Operator, PresetExpression Operand) : PresetExpression
    {
        public override object Evaluate(PresetContext context)
        {
            return Operator switch
            {
                "not" or "!" => !Operand.Boolean(context), "-" => -Operand.Number(context),
                _ => Operand.Number(context),
            };
        }
    }
}
