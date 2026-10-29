namespace RitsukageGif.Scripting
{
    public sealed record PresetVariable(string Name) : PresetExpression
    {
        public override object Evaluate(PresetContext context)
        {
            return Name switch
            {
                "exists" => context.Window is not null, "recording" => context.Recording,
                "busy" => context.Busy, "idle" => !context.Busy,
                "title" => context.Window?.Title ?? "",
                "width" => (double)(context.Window?.Bounds.Width ?? 0),
                "height" => (double)(context.Window?.Bounds.Height ?? 0),
                "elapsed" => context.Elapsed, "recording.elapsed" => context.RecordingElapsed,
                _ when context.Variables?.TryGetValue(Name, out var variable) == true => variable,
                _ => context.Constants.TryGetValue(Name, out var value)
                    ? value
                    : throw new InvalidOperationException(UiText.Format("DslUnknownName", Name)),
            };
        }
    }
}
