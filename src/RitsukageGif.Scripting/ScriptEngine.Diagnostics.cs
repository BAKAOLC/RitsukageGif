namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptEngine
    {
        private readonly List<ScriptStackFrame> _callSites = [];
        private ScriptDiagnostic? _failureLocation;
        internal IReadOnlyList<ScriptStackFrame>? FailureFrames { get; private set; }

        internal ScriptDiagnostic? FailureDiagnostic(string message) =>
            _failureLocation is { } location ? location with { Message = message } : null;

        private void RememberFailure(object? value, ScriptNode node)
        {
            if (value is not ScriptResult { IsOk: false }) return;
            var file = _files.Peek();
            _failureLocation = new ScriptDiagnostic(node.Line, node.Column, "", file);
            FailureFrames = CaptureFrames(node, file);
        }

        private IReadOnlyList<ScriptStackFrame> CaptureFrames(ScriptNode? node, string file) =>
            CaptureFrames(node?.Line ?? 1, node?.Column ?? 1, file);

        private IReadOnlyList<ScriptStackFrame> CaptureFrames(int line, int column, string file)
        {
            var frames = new ScriptStackFrame[_callSites.Count + 1];
            frames[0] = new ScriptStackFrame(_calls.LastOrDefault() ?? "script", file, line, column);
            for (var i = 0; i < _callSites.Count; i++) frames[i + 1] = _callSites[^(i + 1)];
            return frames;
        }
    }
}
