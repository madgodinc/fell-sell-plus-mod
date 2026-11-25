using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FellSellPlus.Info;

// Skills page: hovering an undiscovered "?" tile lists which skills it could be (the undiscovered
// skills of that class and rarity) with each one's chance from the cheapest book tier that can roll
// it. RevealSkillTiles (spoiler, off) names the exact skill behind the tile.
// SkillSlotUI.OnPointerEnter/OnPointerExit are EventSystem interface calls; patching the bodies catches them.
internal static class SkillTileHints
{
    static GameObject _root;
    static TextMeshProUGUI _text;

    public static void Show(SkillSlotUI slot)
    {
        if (!InfoSettings.SkillTileHints.Value || slot == null) return;
        var skill = slot.Skill;
        var state = slot.State;
        if (skill == null || (state != null && state.IsDiscovered)) return;
        if (_root == null)
        {
            _root = InfoUi.Canvas("FSP_SkillTileHint", 700);
            var panel = InfoUi.Panel("Panel", _root.transform, new Vector2(0f, 1f), new Vector2(40, -140), new Vector2(560, 520), new Vector2(0f, 1f), 0.96f);
            _text = InfoUi.Text(panel, "Text", 19, TextAlignmentOptions.TopLeft, 24);
        }
        _text.text = Compose(skill);
        _root.SetActive(true);
    }

    public static void Hide() { if (_root != null && _root.activeSelf) _root.SetActive(false); }

    static string Compose(SkillData target)
    {
        var psm = PlayerSkillManager.Instance;
        var sb = new StringBuilder();
        int rarity = (int)target.rarity;
        sb.Append($"<size=24><color={InfoUi.Hex(InfoUi.Gold)}>{InfoUi.T("Неоткрытый навык", "Undiscovered Skill")}</color></size>\n");
        sb.Append($"<color={InfoUi.RarityHex(rarity)}>{InfoUi.RarityName(rarity)}</color> · {target.GetLocalizedCategoryName()}\n\n");

        // Cheapest tier whose odds include this rarity.
        int tier = rarity <= 1 ? 1 : rarity <= 3 ? 2 : 3;
        var odds = SkillBookOdds.Odds(target.category, tier, out _);
        var candidates = psm.GetSkillsByCategory(target.category).ToArray()
            .Where(s => s != null && (int)s.rarity == rarity)
            .Where(s => { var st = psm.GetSkillState(s.skillId); return st == null || !st.IsDiscovered; })
            .ToList();
        sb.Append(InfoUi.C(InfoUi.Muted, InfoUi.T($"Может оказаться одним из {candidates.Count}:", $"Could be one of {candidates.Count}:"))).Append('\n');
        foreach (var s in candidates)
        {
            var o = odds.FirstOrDefault(x => x.Skill.skillId == s.skillId);
            bool it = InfoSettings.RevealSkillTiles.Value && s.skillId == target.skillId;
            sb.Append(it ? $"<color={InfoUi.Hex(InfoUi.Gold)}>▶ </color>" : "• ")
              .Append($"<color={InfoUi.RarityHex(rarity)}>{s.GetLocalizedName()}</color>  ")
              .Append(InfoUi.C(InfoUi.Muted, $"{InfoUi.T("ранги", "ranks")} {s.maxRank}"))
              .Append(o != null ? InfoUi.C(InfoUi.Muted, $"  · {o.Chance:0.#}% {InfoUi.T($"с книги ур. {tier}", $"from a tier {tier} book")}") : "")
              .Append('\n');
            sb.Append($"<size=16>{InfoUi.C(InfoUi.Muted, s.GetLocalizedDescription())}</size>\n");
        }
        return sb.ToString();
    }
}

[HarmonyPatch(typeof(SkillSlotUI), nameof(SkillSlotUI.OnPointerEnter))]
internal static class SkillTileEnterPatch
{
    static void Postfix(SkillSlotUI __instance)
    {
        try { SkillTileHints.Show(__instance); } catch (Exception e) { Plugin.L.LogWarning($"Skill tile hint: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(SkillSlotUI), nameof(SkillSlotUI.OnPointerExit))]
internal static class SkillTileExitPatch
{
    static void Postfix() => SkillTileHints.Hide();
}

[HarmonyPatch(typeof(SkillSlotUI), "OnDisable")]
internal static class SkillTileDisablePatch
{
    static void Postfix() => SkillTileHints.Hide();
}
