namespace RitsukageGif.Scripting
{
    public sealed record PresetPercent(double Value) : PresetExpression
    {
        public override object Evaluate(PresetContext context) => Value * context.PercentBasis / 100;
    }
}
