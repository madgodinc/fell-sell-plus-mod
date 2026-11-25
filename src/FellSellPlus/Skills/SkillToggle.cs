using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FellSellPlus.Skills;

// Switching learned skills off and on (the base game has no such thing). A switched-off skill keeps its
// rank and the points spent on it, so there is nothing to farm; it only stops working. To keep it from
// being used as a mid-run trick, the switch works only in the village (MAIN). The list is stored in the
// game's own save next to the skill data.
internal static class SkillToggle
{
    const string Key = "FellSellPlus_DisabledSkills";
    public static readonly HashSet<string> Disabled = new();

    public static bool IsOff(string id) => id != null && Disabled.Contains(id);
    public static bool CanChange => SceneManager.GetActiveScene().name == "MAIN";

    public static void Flip(string id)
    {
        if (!CanChange || string.IsNullOrEmpty(id)) return;
        if (!Disabled.Remove(id)) Disabled.Add(id);
        Progression.GameQuery.RefreshPlayerStats();
    }

    public static void Save(string file)
    {
        try { ES3.Save<string>(Key, string.Join(",", Disabled), file); }
        catch (Exception e) { Plugin.L.LogWarning($"Skill toggle save: {e.Message}"); }
    }

    public static void Load(string file)
    {
        Disabled.Clear();
        try
        {
            if (!ES3.KeyExists(Key, file)) return;
            foreach (var id in ES3.Load<string>(Key, file, "").Split(',', StringSplitOptions.RemoveEmptyEntries)) Disabled.Add(id);
        }
        catch (Exception e) { Plugin.L.LogWarning($"Skill toggle load: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(PlayerSkillManager), nameof(PlayerSkillManager.GetSkillRank))]
internal static class SkillToggleRankPatch
{
    static void Postfix(string __0, ref int __result) { if (__result > 0 && SkillToggle.IsOff(__0)) __result = 0; }
}

[HarmonyPatch(typeof(PlayerSkillManager), nameof(PlayerSkillManager.GetPassiveSkillRank))]
internal static class SkillTogglePassivePatch
{
    static void Postfix(string __0, ref int __result) { if (__result > 0 && SkillToggle.IsOff(__0)) __result = 0; }
}

[HarmonyPatch(typeof(PlayerSkillManager), nameof(PlayerSkillManager.HasActiveSkill))]
internal static class SkillToggleActivePatch
{
    static void Postfix(string __0, ref bool __result) { if (__result && SkillToggle.IsOff(__0)) __result = false; }
}

[HarmonyPatch(typeof(PlayerSkillManager), nameof(PlayerSkillManager.SaveData))]
internal static class SkillToggleSavePatch { static void Postfix(string __0) => SkillToggle.Save(__0); }

[HarmonyPatch(typeof(PlayerSkillManager), nameof(PlayerSkillManager.RestoreData))]
internal static class SkillToggleLoadPatch { static void Postfix(string __0) => SkillToggle.Load(__0); }

// The switch sits in the skills page inspector, under the game's upgrade button. Off skills are
// drawn faded in the pyramid.
[HarmonyPatch(typeof(PlayerSkillsUI), "UpdateInspector")]
internal static class SkillToggleInspectorPatch
{
    static Button _btn;
    static TextMeshProUGUI _label;
    static string _id;

    static void Postfix(PlayerSkillsUI __instance, SkillData __0, PlayerSkillManager.SkillProgressState __1)
    {
        try
        {
            _id = __0?.skillId;
            bool learned = __0 != null && __1 != null && __1.currentActiveRank > 0;
            if (_btn == null && learned) Create(__instance);
            if (_btn == null) return;
            _btn.gameObject.SetActive(learned);
            if (!learned) return;
            Paint();
        }
        catch (Exception e) { Plugin.L.LogWarning($"Skill toggle UI: {e.Message}"); }
    }

    static void Paint()
    {
        bool ru = SkillRegistry.Russian();
        bool off = SkillToggle.IsOff(_id);
        if (!SkillToggle.CanChange)
        {
            _label.text = off ? (ru ? "Отключён · менять можно в деревне" : "Off · change it in the village")
                              : (ru ? "Включён · менять можно в деревне" : "On · change it in the village");
            _btn.interactable = false;
        }
        else
        {
            _label.text = off ? (ru ? "Включить навык" : "Turn skill on") : (ru ? "Отключить навык" : "Turn skill off");
            _btn.interactable = true;
        }
        var img = _btn.image;
        if (img != null) img.color = off ? new Color(0.45f, 0.75f, 0.40f, 1f) : new Color(0.55f, 0.50f, 0.45f, 1f);
        TextFit.Line(_label, _label.fontSize);
    }

    static void Create(PlayerSkillsUI ui)
    {
        var src = ui._upgradeButton;
        if (src == null) return;
        var go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
        go.name = "FSP_SkillToggle";
        var rt = go.GetComponent<RectTransform>();
        var srt = src.GetComponent<RectTransform>();
        // Directly under the upgrade button, same width.
        rt.anchoredPosition = srt.anchoredPosition - new Vector2(0f, srt.rect.height + 10f);
        _btn = go.GetComponent<Button>();
        _btn.onClick = new Button.ButtonClickedEvent();
        _btn.onClick.AddListener((Action)(() =>
        {
            SkillToggle.Flip(_id);
            Paint();
            ui.RefreshAll();
        }));
        _label = go.GetComponentInChildren<TextMeshProUGUI>(true);
        foreach (var c in go.GetComponentsInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true))
            UnityEngine.Object.Destroy(c);
    }
}

[HarmonyPatch(typeof(PlayerSkillsUI), nameof(PlayerSkillsUI.RefreshAll))]
internal static class SkillToggleSlotsPatch
{
    static void Postfix(PlayerSkillsUI __instance)
    {
        try
        {
            var slots = __instance._activePyramidSlots;
            if (slots == null) return;
            foreach (var s in slots)
            {
                if (s == null || s._iconImage == null) continue;
                bool off = s._skillData != null && SkillToggle.IsOff(s._skillData.skillId);
                var c = s._iconImage.color; c.a = off ? 0.3f : 1f; s._iconImage.color = c;
            }
        }
        catch { }
    }
}
