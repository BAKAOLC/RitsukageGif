namespace RitsukageGif.Contracts
{
    public interface IFormulaOutputService
    {
        Task<FormulaRenderResult> RenderAsync(IReadOnlyList<string> sources, FormulaImageFormat format,
            float fontSize = 28, uint textColor = 0xff000000, CancellationToken token = default);

        Task<FormulaRenderResult> ConvertAsync(IReadOnlyList<string> sources, FormulaTextFormat format,
            CancellationToken token = default);
    }
}
