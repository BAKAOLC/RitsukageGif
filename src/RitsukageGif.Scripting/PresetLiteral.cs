namespace RitsukageGif.Scripting
{
    public sealed record PresetLiteral(object Value) : PresetExpression
    {
        public override object Evaluate(PresetContext context) => Value;
    }
}
