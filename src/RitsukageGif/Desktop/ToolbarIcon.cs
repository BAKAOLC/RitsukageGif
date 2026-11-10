using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    internal static class ToolbarIcon
    {
        public static IconDefinition? FindDefinition(string key)
        {
            return IconCatalog.FindDefinition(key);
        }

        public static IconDefinition GetDefinition(string key)
        {
            return IconCatalog.GetDefinition(key);
        }

        public static Control Create(TemplatedControl owner, string key)
        {
            return Create(owner, GetDefinition(key));
        }

        public static Control Create(TemplatedControl owner, IconDefinition definition)
        {
            return CreateInViewport(owner, definition);
        }

        public static Control CreateInViewport(TemplatedControl owner, IconDefinition definition)
        {
            return new SvgIcon(definition)
            {
                [!TemplatedControl.ForegroundProperty] = owner[!TemplatedControl.ForegroundProperty],
            };
        }
    }
}
