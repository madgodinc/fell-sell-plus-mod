using Il2CppTMPro;

namespace FellSellPlus;

// One rule for every mod text so nothing spills out of its box in either language:
// text first shrinks (down to 70% of its size), then single lines end in an ellipsis.
internal static class TextFit
{
    public static void Line(TextMeshProUGUI t, float size)
    {
        if (t == null) return;
        t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = size * 0.7f;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
    }

    public static void Block(TextMeshProUGUI t, float size)
    {
        if (t == null) return;
        t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = size * 0.7f;
        t.enableWordWrapping = true;
        t.overflowMode = TextOverflowModes.Ellipsis;
    }
}
