namespace Avalonia.Native
{
    internal static class Extensions
    {
        public static int AsComBool(this bool b)
        {
            return b ? 1 : 0;
        }

        public static bool FromComBool(this int b)
        {
            return b != 0;
        }
    }
}
