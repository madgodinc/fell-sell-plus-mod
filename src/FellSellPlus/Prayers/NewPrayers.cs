using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Localization;

namespace FellSellPlus.Prayers;

// New prayers built from the game's own PrayerData effect fields, so the game's
// PrayersManager.RecalculateStats applies them exactly like vanilla ones. Their LocalizedStrings
// have no table entry; LocalizedString.GetLocalizedString is intercepted for exactly those
// instances and returns the mod's text, so every place the game shows a prayer name works.
// They join the matching statue pool (good/evil) and the prayer database; saves store them by
// asset name, resolved through GetPrayerByName.
internal static class NewPrayers
{
    public static ConfigEntry<bool> Enabled;

    sealed class Def
    {
        public string Id, Icon, NameRu, NameEn, DescRu, DescEn;
        public bool Evil;
        public Action<PrayerData> Apply;
    }

    static readonly Def[] Defs =
    {
        new() { Id = "fsp_prayer_mail_of_faith", Icon = "Iron Skin", NameRu = "Кольчуга веры", NameEn = "Mail of Faith",
            DescRu = "+25 брони\n+5% Снижение урона", DescEn = "+25 Armor\n+5% Damage reduction",
            Apply = p => { p.armorBonus = 25f; p.damageReduction = 0.05f; } },
        new() { Id = "fsp_prayer_second_wind", Icon = "Fast Recovery", NameRu = "Второе дыхание", NameEn = "Second Wind",
            DescRu = "+20 макс. выносливости\n+40% регенерации выносливости", DescEn = "+20 Max Stamina\n+40% Stamina regeneration",
            Apply = p => { p.maxStaminaBonus = 20f; p.staminaRegenBonus = 0.4f; } },
        new() { Id = "fsp_prayer_keen_edge", Icon = "Critical Eye", NameRu = "Острый клинок", NameEn = "Keen Edge",
            DescRu = "+15% больше наносимого физического урона\n+10% шанс крита", DescEn = "+15% more physical damage dealt\n+10% Crit Chance",
            Apply = p => { p.physicalDamageBonus = 0.15f; p.critChanceBonus = 10f; } },
        new() { Id = "fsp_prayer_sage", Icon = "Divine Mana", NameRu = "Мудрец", NameEn = "Sage",
            DescRu = "+40 макс. маны\n+30% регенерации маны", DescEn = "+40 Max Mana\n+30% Mana regeneration",
            Apply = p => { p.maxManaBonus = 40f; p.manaRegenBonus = 0.3f; } },
        new() { Id = "fsp_prayer_tailwind", Icon = "Runner", NameRu = "Попутный ветер", NameEn = "Tailwind",
            DescRu = "+15% Скорость передвижения", DescEn = "+15% Movement Speed",
            Apply = p => { p.movementSpeedBonus = 0.15f; } },
        new() { Id = "fsp_prayer_gold_vein", Icon = "Lucky", NameRu = "Золотая жила", NameEn = "Gold Vein",
            DescRu = "+30% шанс выпадения", DescEn = "+30% Drop Chance",
            Apply = p => { p.dropChanceBonus = 30f; } },

        new() { Id = "fsp_prayer_glass_cannon", Icon = "Berserker", Evil = true, NameRu = "Стеклянная пушка", NameEn = "Glass Cannon",
            DescRu = "+60% урона (физического и магического)\n-40% макс. здоровья", DescEn = "+60% damage (Physical and Magical)\n-40% Max Health",
            Apply = p => { p.physicalDamageBonus = 0.6f; p.magicalDamageBonus = 0.6f; p.maxHealthMultiplier = 0.6f; } },
        new() { Id = "fsp_prayer_greed", Icon = "High Roller", Evil = true, NameRu = "Жадность", NameEn = "Greed",
            DescRu = "+100% шанс выпадения\n-Врагов на этаже вдвое больше", DescEn = "+100% Drop Chance\n-Twice as many enemies per floor",
            Apply = p => { p.dropChanceBonus = 100f; p.enemySpawnMultiplier = 2f; } },
        new() { Id = "fsp_prayer_blood_pact", Icon = "Vampire", Evil = true, NameRu = "Кровавый договор", NameEn = "Blood Pact",
            DescRu = "+Похищение здоровья: 20% нанесённого урона\n-Теряйте 1% здоровья каждую секунду", DescEn = "+Lifedrain 20% of damage dealt\n-Lose 1% Health every second",
            Apply = p => { p.lifeDrainPercent = 20f; p.hpDrainPercentPerSecond = 1f; } },
        new() { Id = "fsp_prayer_last_stand", Icon = "Cat", Evil = true, NameRu = "Последний рубеж", NameEn = "Last Stand",
            DescRu = "+3 спас-броска от смерти\n-Получаемый урон +50%", DescEn = "+3 Death Saves\n-Damage Received +50%",
            Apply = p => { p.deathSavesCount = 3; p.damageTakenMultiplier = 1.5f; } },
        new() { Id = "fsp_prayer_hurricane", Icon = "Speedster", Evil = true, NameRu = "Ураган", NameEn = "Hurricane",
            DescRu = "+35% Скорость передвижения\n+15% скорость атаки\n-25% макс. здоровья", DescEn = "+35% Movement Speed\n+15% Attack Speed\n-25% Max Health",
            Apply = p => { p.movementSpeedBonus = 0.35f; p.attackSpeedBonus = 0.15f; p.maxHealthMultiplier = 0.75f; } },
    };

    static readonly Dictionary<string, Def> ById = new();
    // LocalizedString instance -> (prayer id, true = name / false = description).
    internal static readonly Dictionary<IntPtr, (string id, bool name)> Texts = new();
    static readonly List<PrayerData> Made = new();

    public static void Bind(ConfigFile cfg) =>
        Enabled = cfg.Bind("Prayers", "NewPrayers", true, "Add the mod's new prayers to the statues (6 good, 5 evil).");

    public static bool IsOurs(PrayerData p) => p != null && p.name != null && ById.ContainsKey(p.name);

    public static string Name(PrayerData p) { var d = ById[p.name]; return Ru ? d.NameRu : d.NameEn; }
    public static string Text(string id, bool name) { var d = ById[id]; return name ? (Ru ? d.NameRu : d.NameEn) : (Ru ? d.DescRu : d.DescEn); }
    public static string Desc(PrayerData p) { var d = ById[p.name]; return Ru ? d.DescRu : d.DescEn; }
    static bool Ru => Progression.GameQuery.IsRussian();

    // Built once, after the vanilla database exists (icons are borrowed from it).
    public static List<PrayerData> All(Il2CppSystem.Collections.Generic.List<PrayerData> vanilla)
    {
        if (Made.Count > 0 || !Enabled.Value) return Made;
        foreach (var d in Defs)
        {
            var p = ScriptableObject.CreateInstance<PrayerData>(); // the constructor sets neutral multipliers
            p.name = d.Id;
            p.hideFlags = HideFlags.DontUnloadUnusedAsset;
            p.localizedName = new LocalizedString();
            p.localizedDescription = new LocalizedString();
            Texts[p.localizedName.Pointer] = (d.Id, true);
            Texts[p.localizedDescription.Pointer] = (d.Id, false);
            p.isEvil = d.Evil;
            d.Apply(p);
            if (vanilla != null)
                for (int i = 0; i < vanilla.Count; i++)
                    if (vanilla[i] != null && vanilla[i].name == d.Icon) { p.icon = vanilla[i].icon; break; }
            ById[d.Id] = d;
            Made.Add(p);
        }
        Plugin.L.LogInfo($"New prayers ready: {Made.Count}.");
        return Made;
    }

    public static void AddTo(Il2CppSystem.Collections.Generic.List<PrayerData> list, Func<PrayerData, bool> filter)
    {
        if (list == null) return;
        foreach (var p in Made)
        {
            if (!filter(p)) continue;
            bool has = false;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].Pointer == p.Pointer) { has = true; break; }
            if (!has) list.Add(p);
        }
    }
}

// The manager's database feeds RollRandomPrayers (initial and forced prayers) and save lookups.
[HarmonyPatch(typeof(PrayersManager), "Awake")]
internal static class PrayerDatabasePatch
{
    static void Postfix(PrayersManager __instance)
    {
        if (!NewPrayers.Enabled.Value) return;
        NewPrayers.All(__instance.allPrayersDatabase);
        NewPrayers.AddTo(__instance.allPrayersDatabase, _ => true);
    }
}

// Statue pools: good statues get the good ones, evil statues (any evil prayer in the pool) the evil ones.
[HarmonyPatch(typeof(PrayersStatue), nameof(PrayersStatue.RollBoons))]
internal static class StatuePoolPatch
{
    static void Prefix(PrayersStatue __instance)
    {
        if (!NewPrayers.Enabled.Value) return;
        var pool = __instance.boonPool;
        if (pool == null || pool.Count == 0) return;
        NewPrayers.All(PrayersManager.Instance?.allPrayersDatabase);
        bool evilStatue = false;
        for (int i = 0; i < pool.Count; i++) if (pool[i] != null && pool[i].isEvil && !NewPrayers.IsOurs(pool[i])) { evilStatue = true; break; }
        NewPrayers.AddTo(pool, p => p.isEvil == evilStatue);
    }
}

// Saves keep prayers by asset name.
[HarmonyPatch(typeof(PrayersManager), nameof(PrayersManager.GetPrayerByName))]
internal static class PrayerByNamePatch
{
    static void Postfix(string name, ref PrayerData __result)
    {
        if (__result != null || name == null) return;
        foreach (var p in NewPrayers.All(PrayersManager.Instance?.allPrayersDatabase))
            if (p.name == name) { __result = p; return; }
    }
}

// Without this the game throws "Empty Table Reference" inside PrayerCard.Setup for our prayers.
[HarmonyPatch(typeof(LocalizedString), nameof(LocalizedString.GetLocalizedString), new Type[0])]
internal static class PrayerLocalizedTextPatch
{
    static bool Prefix(LocalizedString __instance, ref string __result)
    {
        if (NewPrayers.Texts.Count == 0 || __instance == null) return true;
        if (!NewPrayers.Texts.TryGetValue(__instance.Pointer, out var t)) return true;
        __result = NewPrayers.Text(t.id, t.name);
        return false;
    }
}

[HarmonyPatch(typeof(PrayerCard), nameof(PrayerCard.Setup))]
internal static class PrayerCardTextPatch
{
    static void Postfix(PrayerCard __instance, PrayerData data)
    {
        if (!NewPrayers.IsOurs(data)) return;
        if (__instance.nameText != null) __instance.nameText.text = NewPrayers.Name(data);
        if (__instance.descText != null) __instance.descText.text = NewPrayers.Desc(data);
    }
}

[HarmonyPatch(typeof(ActivePrayersUI), nameof(ActivePrayersUI.ShowTooltip))]
internal static class PrayerTooltipTextPatch
{
    static void Postfix(ActivePrayersUI __instance, PrayerData prayer)
    {
        if (!NewPrayers.IsOurs(prayer)) return;
        if (__instance.tooltipTitle != null) __instance.tooltipTitle.text = NewPrayers.Name(prayer);
        if (__instance.tooltipDescription != null) __instance.tooltipDescription.text = NewPrayers.Desc(prayer);
    }
}
