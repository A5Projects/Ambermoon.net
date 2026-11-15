using Ambermoon.Render;

namespace Ambermoon.UI
{
    /// <summary>
    /// Opt-in contrast treatment for narrative panels, without changing shared UI palettes.
    /// </summary>
    internal static class ReadingTextStyle
    {
        public const byte ShadowOpacity = 96;

        public static Color Background(Color color) => new(
            color.R * 4 / 5, color.G * 4 / 5, color.B * 4 / 5, color.A);
    }
}
