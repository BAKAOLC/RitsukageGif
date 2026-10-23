namespace RitsukageGif.Scripting
{
    public sealed record PresetCall(string Name, IReadOnlyList<PresetExpression> Arguments) : PresetExpression
    {
        public override object Evaluate(PresetContext context)
        {
            double N(int i) => Arguments[i].Number(context);
            return Name switch
            {
                "min" => Math.Min(N(0), N(1)), "max" => Math.Max(N(0), N(1)),
                "clamp" => Math.Clamp(N(0), N(1), N(2)), "abs" => Math.Abs(N(0)),
                "round" => Math.Round(N(0)), "floor" => Math.Floor(N(0)), "ceil" => Math.Ceiling(N(0)),
                _ => throw new InvalidOperationException(UiText.Format("DslUnknownName", Name))
            };
        }
    }
}
