using HarmonyLib;
using UnityEngine;

namespace FellSellPlus;

// The game's "look at" name box stays exactly as the game draws it. When the mod adds a second
// line under a name (ground item worth, filter state), the text is shrunk to fit inside that same
// frame with TMP auto-size instead of the frame being resized. Plain names get the game's own font
// size back. UpdateInteractionUI (0x18042F110) runs every frame something is looked at.
[HarmonyPatch(typeof(FirstPersonController), "UpdateInteractionUI")]
internal static class InteractionBoxFitPatch
{
    static float _size = -1f;
    static bool _auto;

    static void Postfix(FirstPersonController __instance)
    {
        try
        {
            var text = __instance.interactionText;
            if (text == null || !text.gameObject.activeInHierarchy) return;
            bool extra = text.text != null && text.text.Contains('\n');
            if (extra == _auto) return;
            if (extra)
            {
                if (_size < 0f || !text.enableAutoSizing) _size = text.fontSize;
                text.fontSizeMax = _size;
                text.fontSizeMin = _size * 0.4f;
                text.enableAutoSizing = true;
            }
            else
            {
                text.enableAutoSizing = false;
                if (_size > 0f) text.fontSize = _size;
            }
            _auto = extra;
        }
        catch { }
    }
}
