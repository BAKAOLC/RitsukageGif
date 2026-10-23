namespace RitsukageGif.Scripting
{
    public abstract record PresetExpression
    {
        public abstract object Evaluate(PresetContext context);

        public double Number(PresetContext context) => Evaluate(context) is double n && double.IsFinite(n)
            ? n
            : throw new InvalidOperationException(UiText.Get("DslNumberRequired"));

        public bool Boolean(PresetContext context) => Evaluate(context) is bool b
            ? b
            : throw new InvalidOperationException(UiText.Get("DslBooleanRequired"));
    }
}
