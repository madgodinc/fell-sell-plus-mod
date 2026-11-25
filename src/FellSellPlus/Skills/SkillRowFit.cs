using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FellSellPlus.Skills;

// The skills window builds one row per rarity (PlayerSkillsUI.FillRow 0x180566080, called 5 times by
// BuildPyramidForCategory) with one slot per skill. Vanilla rows hold at most 4 slots; with the mod's
// skills a row can hold 5 or more, so a row wider than its container is scaled down to fit.
[HarmonyPatch(typeof(PlayerSkillsUI), "FillRow")]
internal static class SkillRowFitPatch
{
    static void Postfix(PlayerSkillsUI __instance, string rowName)
    {
        try
        {
            var container = __instance._pyramidRowsContainer;
            var row = container != null ? container.Find(rowName) : null;
            if (row == null) return;
            var rowRt = row.GetComponent<RectTransform>();
            var parentRt = container.GetComponent<RectTransform>();
            if (rowRt == null || parentRt == null) return;

            float total = 0f; int n = 0;
            for (int i = 0; i < row.childCount; i++)
            {
                var c = row.GetChild(i);
                if (!c.gameObject.activeSelf) continue;
                var rt = c.GetComponent<RectTransform>();
                total += rt != null ? rt.rect.width > 0f ? rt.rect.width : rt.sizeDelta.x : 0f;
                n++;
            }
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout != null && n > 1) total += layout.spacing * (n - 1) + layout.padding.left + layout.padding.right;

            float avail = parentRt.rect.width * 0.96f;
            float scale = total > avail && total > 0f ? avail / total : 1f;
            rowRt.localScale = new Vector3(scale, scale, 1f);
        }
        catch (Exception e) { Plugin.L.LogWarning($"Skill row fit: {e.Message}"); }
    }
}
