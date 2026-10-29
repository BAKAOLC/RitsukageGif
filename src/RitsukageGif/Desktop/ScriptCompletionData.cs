using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace RitsukageGif.Desktop
{
    internal sealed class ScriptCompletionData(ScriptCompletion item) : ICompletionData
    {
        public IImage? Image => null;
        public string Text => item.Label;
        public object Content => item.Label;
        public object Description => item.Detail;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, item.InsertText);
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ScriptCompletionData))]
        public static ScriptCompletionData Create(ScriptCompletion item)
        {
            return new(item);
        }
    }
}
