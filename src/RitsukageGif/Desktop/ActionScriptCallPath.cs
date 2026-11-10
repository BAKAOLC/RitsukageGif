namespace RitsukageGif.Desktop
{
    internal sealed class ActionScriptCallPath(string id, ActionScriptCallPath? parent)
    {
        private string Id { get; } = id;
        private ActionScriptCallPath? Parent { get; } = parent;

        internal bool Contains(string actionId)
        {
            for (var current = this; current is not null; current = current.Parent)
                if (string.Equals(current.Id, actionId, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
