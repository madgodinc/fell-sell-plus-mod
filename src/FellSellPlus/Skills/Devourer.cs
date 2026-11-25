using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Skills;

// Devourer (Occultist legendary): during a dungeon/forest run, eat an inventory item (Consume in the
// item menu, or hold X over it) for a bonus until the player is back in town. The bonus comes from what the item itself does,
// at 10%: a potion healing 30 gives +3 max Health, a 20-damage sword +2% damage, and so on.
// Anvil upgrades add 25% per level; items with no stats give a little Health by rarity.
// Every item's result is listed in UserData/FellSellPlus/devour_table.txt; devour_overrides.json
// replaces the rule for chosen items.
internal static class Devourer
{
    public enum Stat { MaxHealth, MaxStamina, MaxMana, HealthRegen, StaminaRegen, ManaRegen, Damage, Armor, AttackSpeed, MoveSpeed, DropChance }

    const float Share = 0.10f, PerUpgrade = 0.25f, HoldSeconds = 0.6f;
    static readonly float[] NoStatsHealth = { 1f, 2f, 3f, 5f, 8f, 12f };

    // Run caps per stat (pools and regeneration uncapped, except Health regeneration at +10/s).
    const float NoCap = float.MaxValue;
    static readonly Dictionary<Stat, float> Cap = new()
    {
        [Stat.MaxHealth] = NoCap, [Stat.MaxStamina] = NoCap, [Stat.MaxMana] = NoCap,
        [Stat.HealthRegen] = 10, [Stat.StaminaRegen] = NoCap, [Stat.ManaRegen] = NoCap,
        [Stat.Damage] = 50, [Stat.Armor] = 50, [Stat.AttackSpeed] = 25, [Stat.MoveSpeed] = 25, [Stat.DropChance] = 50,
    };

    public static readonly Dictionary<Stat, float> Total = new();
    public static float Get(Stat s) => Total.TryGetValue(s, out var v) ? v : 0f;

    static ItemData _hover;
    static float _holdStart = -1f;

    static bool InRun()
    {
        var s = SceneManager.GetActiveScene().name;
        return s == "DUNGEON" || s == "FOREST";
    }

    public static bool Active => InRun() && SkillRegistry.Rank(SkillRegistry.OccultDevourer) > 0;

    public static void NoteHover(ItemData item) => _hover = item;
    public static void ClearHover() { _hover = null; _holdStart = -1f; }

    // ---- the rule ----

    public static List<(Stat stat, float amount)> BonusFor(ItemData item, int upgradeLevel)
    {
        var list = new List<(Stat, float)>();
        if (Overrides().TryGetValue(item.name, out var ov))
        {
            foreach (var kv in ov) if (Enum.TryParse<Stat>(kv.Key, true, out var st)) list.Add((st, kv.Value));
            return list;
        }

        void Add(Stat s, float raw) { if (Math.Abs(raw) > 0.0001f) list.Add((s, raw)); }
        // Instant effects -> pool size.
        Add(Stat.MaxHealth, item.healAmount * Share);
        Add(Stat.MaxMana, item.instantManaRestore * Share);
        // Effects over time -> regeneration.
        Add(Stat.HealthRegen, item.healOverTime * Share);
        Add(Stat.StaminaRegen, item.staminaRegenOverTime * Share);
        Add(Stat.ManaRegen, item.manaRegenOverTime * Share);
        // Weapons and armor.
        if (item.itemType == ItemType.Weapon) Add(Stat.Damage, item.damage * Share);
        Add(Stat.Armor, item.armor * Share);
        // Buff modifiers are fractions (0.2 = +20%) -> percent points.
        Add(Stat.Damage, item.damageDealtModifier * 100f * Share);
        Add(Stat.AttackSpeed, item.consumableAttackSpeedModifier * 100f * Share);
        Add(Stat.MoveSpeed, item.consumableMoveSpeedModifier * 100f * Share);
        // Furniture dungeon bonuses.
        Add(Stat.MaxHealth, item.dungeonBonusHealth * Share);
        Add(Stat.MaxStamina, item.dungeonBonusStamina * Share);
        Add(Stat.MaxMana, item.dungeonBonusMana * Share);
        Add(Stat.Armor, item.dungeonBonusArmor * Share);
        Add(Stat.StaminaRegen, item.dungeonStaminaRegen * Share);
        Add(Stat.ManaRegen, item.dungeonManaRegen * Share);
        Add(Stat.MoveSpeed, item.dungeonMoveSpeed * Share);
        Add(Stat.AttackSpeed, item.dungeonAttackSpeed * Share);
        Add(Stat.Damage, item.dungeonBonusAttackDamage * Share);
        Add(Stat.DropChance, item.dungeonDropChance * Share);

        if (list.Count == 0)
            list.Add((Stat.MaxHealth, NoStatsHealth[Math.Clamp((int)item.rarity, 0, NoStatsHealth.Length - 1)]));

        float mul = 1f + PerUpgrade * Math.Max(upgradeLevel, item.upgradeLevel);
        // Merge duplicates (e.g. damage from both the weapon and a modifier).
        return list.GroupBy(x => x.Item1).Select(g => (g.Key, g.Sum(x => x.Item2) * mul)).ToList();
    }

    internal static string ShortLabel(Stat s, float v, bool ru) => Label(s, v, ru);

    static string Label(Stat s, float v, bool ru) => s switch
    {
        Stat.MaxHealth => ru ? $"+{v:0.#} к макс. здоровью" : $"+{v:0.#} Max Health",
        Stat.MaxStamina => ru ? $"+{v:0.#} к макс. выносливости" : $"+{v:0.#} Max Stamina",
        Stat.MaxMana => ru ? $"+{v:0.#} к макс. мане" : $"+{v:0.#} Max Mana",
        Stat.HealthRegen => ru ? $"+{v:0.##}/с восстановления здоровья" : $"+{v:0.##}/s Health Regeneration",
        Stat.StaminaRegen => ru ? $"+{v:0.##}/с восстановления выносливости" : $"+{v:0.##}/s Stamina Regeneration",
        Stat.ManaRegen => ru ? $"+{v:0.##}/с регенерации маны" : $"+{v:0.##}/s Mana Regeneration",
        Stat.Damage => ru ? $"+{v:0.#}% урона" : $"+{v:0.#}% damage",
        Stat.Armor => ru ? $"+{v:0.#} к броне" : $"+{v:0.#} Armor",
        Stat.AttackSpeed => ru ? $"+{v:0.#}% к скорости атаки" : $"+{v:0.#}% Attack Speed",
        Stat.MoveSpeed => ru ? $"+{v:0.#}% к скорости передвижения" : $"+{v:0.#}% Move Speed",
        _ => ru ? $"+{v:0.#}% к шансу выпадения" : $"+{v:0.#}% Drop Chance",
    };

    static string Effects(ItemData item, int upgrade, bool ru) =>
        string.Join(", ", BonusFor(item, upgrade).Select(b => Label(b.stat, b.amount, ru)));

    public static string Hint(ItemData item)
    {
        bool ru = SkillRegistry.Russian();
        return (ru ? "Удерживайте X: поглотить → " : "Hold X: consume → ") + Effects(item, SlotOf(item)?.upgradeLevel ?? 0, ru);
    }

    // ---- per frame ----

    public static void Tick()
    {
        var scene = SceneManager.GetActiveScene().name;
        if (scene == "MAIN" && Total.Count > 0)
        {
            Total.Clear(); // the feast ends in town
            Progression.GameQuery.RefreshPlayerStats();
        }
        if (InRun()) WriteTable();
        if (_hover == null || !Active) { _holdStart = -1f; return; }
        var kb = Keyboard.current;
        if (kb == null || !kb.xKey.isPressed) { _holdStart = -1f; return; }
        if (_holdStart < 0f) { _holdStart = Time.unscaledTime; return; }
        if (Time.unscaledTime - _holdStart < HoldSeconds) return;
        _holdStart = -1f;
        Consume(_hover);
    }

    static InventorySlot SlotOf(ItemData item)
    {
        foreach (var s in Inventory.ItemCounts.CarriedSlots())
            if (s != null && s.item != null && s.item.Pointer == item.Pointer && s.quantity > 0 && !s.isEquipped) return s;
        return null;
    }

    internal static void Consume(ItemData item)
    {
        var inv = InventoryManager.Instance;
        var slot = SlotOf(item);
        if (inv == null || slot == null) return;
        bool ru = SkillRegistry.Russian();

        var gained = new List<string>();
        var bonuses = BonusFor(item, slot.upgradeLevel);
        bool anyRoom = bonuses.Any(b => Get(b.stat) < Cap[b.stat]);
        if (!anyRoom) { Notify(item, ru ? "предел насыщения" : "already full", new Color(1f, 0.6f, 0.4f)); return; }

        inv.RemoveItemAmount(slot, 1);
        foreach (var (stat, amount) in bonuses)
        {
            float add = Math.Min(amount, Cap[stat] - Get(stat));
            if (add <= 0f) continue;
            Total[stat] = Get(stat) + add;
            gained.Add(Label(stat, add, ru));
        }
        Progression.GameQuery.RefreshPlayerStats();
        Notify(item, string.Join(", ", gained), new Color(0.75f, 0.45f, 1f));
        _hover = null;
    }

    static void Notify(ItemData item, string what, Color c)
    {
        string name = item.GetDisplayName();
        string title = SkillRegistry.Russian() ? $"Поглощено: {name} ({what})" : $"Consumed: {name} ({what})";
        PickupNotificationManager.Instance?.NotifyCustom(item.icon, title, c, 1);
    }

    // ---- table and overrides ----

    static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus");
    static bool _tableWritten;
    static Dictionary<string, Dictionary<string, float>> _overrides = new();
    static DateTime _overridesStamp;
    static float _nextOverrideCheck;

    static Dictionary<string, Dictionary<string, float>> Overrides()
    {
        if (Time.unscaledTime < _nextOverrideCheck) return _overrides;
        _nextOverrideCheck = Time.unscaledTime + 3f;
        try
        {
            var path = Path.Combine(Dir, "devour_overrides.json");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(path, "{\n  \"_help\": { \"MaxHealth\": 0 },\n  \"Example_Item_Id\": { \"MaxHealth\": 5, \"Damage\": 2 }\n}\n");
            }
            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp != _overridesStamp)
            {
                _overridesStamp = stamp;
                _overrides = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, float>>>(File.ReadAllText(path),
                    new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new();
            }
        }
        catch (Exception e) { Plugin.L.LogWarning($"devour_overrides.json not loaded: {e.Message}"); }
        return _overrides;
    }

    static void WriteTable()
    {
        if (_tableWritten) return;
        var items = ItemDatabaseUtility._database?.allItems;
        if (items == null || items.Count == 0) return;
        _tableWritten = true;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# What every item gives when consumed with Devourer (10% of what the item does; +25% per anvil level).");
            sb.AppendLine("# To change an item, put its id into devour_overrides.json, e.g. \"Health Potion_Data\": { \"MaxHealth\": 5 }.");
            sb.AppendLine("# Stats: " + string.Join(", ", Enum.GetNames(typeof(Stat))));
            sb.AppendLine("# id\trarity\ttype\tRussian\tEnglish");
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) continue;
                sb.AppendLine($"{it.name}\t{it.rarity}\t{it.itemType}\t{Effects(it, 0, true)}\t{Effects(it, 0, false)}");
            }
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "devour_table.txt"), sb.ToString());
        }
        catch (Exception e) { Plugin.L.LogWarning($"devour_table.txt not written: {e.Message}"); }
    }
}

// Hovered item and the hint line in the item tooltip.
[HarmonyPatch(typeof(InventoryTooltipUI), nameof(InventoryTooltipUI.ShowTooltip))]
internal static class DevourerTooltipPatch
{
    static void Prefix(ref string content, ItemData item)
    {
        Devourer.NoteHover(item);
        if (item == null || !Devourer.Active) return;
        content += $"\n<color=#c79bff>{Devourer.Hint(item)}</color>";
    }
}

[HarmonyPatch(typeof(InventoryTooltipUI), nameof(InventoryTooltipUI.HideTooltip))]
internal static class DevourerTooltipHidePatch
{
    static void Postfix() => Devourer.ClearHover();
}

// Max pools: raised only for the duration of RecalculateAllStats (outermost call).
[HarmonyPatch(typeof(PlayerStats), "RecalculateAllStats")]
internal static class DevourerPoolsPatch
{
    static int _depth;
    static void Prefix(PlayerStats __instance) { if (_depth++ == 0) Shift(__instance, 1f); }
    static void Postfix(PlayerStats __instance) { if (--_depth == 0) Shift(__instance, -1f); }

    static void Shift(PlayerStats p, float sign)
    {
        if (Devourer.Total.Count == 0) return;
        p._baseMaxHealth += sign * Devourer.Get(Devourer.Stat.MaxHealth);
        p._baseMaxStamina += sign * Devourer.Get(Devourer.Stat.MaxStamina);
        p._baseMaxMana += sign * Devourer.Get(Devourer.Stat.MaxMana);
    }
}

// Regeneration: raised only while PlayerStats.Update runs.
[HarmonyPatch(typeof(PlayerStats), "Update")]
internal static class DevourerRegenPatch
{
    static int _depth;
    static void Prefix(PlayerStats __instance) { if (_depth++ == 0) Shift(__instance, 1f); }
    static void Postfix(PlayerStats __instance) { if (--_depth == 0) Shift(__instance, -1f); }

    static void Shift(PlayerStats p, float sign)
    {
        if (Devourer.Total.Count == 0) return;
        p._healthRegenRate += sign * Devourer.Get(Devourer.Stat.HealthRegen);
        p._staminaRegenRate += sign * Devourer.Get(Devourer.Stat.StaminaRegen);
        p._manaRegenRate += sign * Devourer.Get(Devourer.Stat.ManaRegen);
    }
}

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.OutgoingDamageMultiplier), MethodType.Getter)]
internal static class DevourerDamagePatch
{
    static void Postfix(ref float __result) { var d = Devourer.Get(Devourer.Stat.Damage); if (d > 0f) __result *= 1f + d / 100f; }
}

// GetTotalArmor (0x180784980): used by TakeDamage, the armor reduction and the armor display.
[HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.GetTotalArmor))]
internal static class DevourerArmorPatch
{
    static void Postfix(ref int __result) { var a = Devourer.Get(Devourer.Stat.Armor); if (a > 0f) __result += Mathf.RoundToInt(a); }
}

[HarmonyPatch(typeof(PlayerSkillEffectsController), nameof(PlayerSkillEffectsController.GetAttackSpeedBonusMultiplier))]
internal static class DevourerAttackSpeedPatch
{
    static void Postfix(ref float __result) { var a = Devourer.Get(Devourer.Stat.AttackSpeed); if (a > 0f) __result *= 1f + a / 100f; }
}

[HarmonyPatch(typeof(FirstPersonController), "CurrentSpeed", MethodType.Getter)]
internal static class DevourerMoveSpeedPatch
{
    static void Postfix(ref float __result) { var m = Devourer.Get(Devourer.Stat.MoveSpeed); if (m > 0f) __result *= 1f + m / 100f; }
}

[HarmonyPatch(typeof(PrayersManager), nameof(PrayersManager.GetDropChanceBonus))]
internal static class DevourerDropPatch
{
    static void Postfix(ref float __result) => __result += Devourer.Get(Devourer.Stat.DropChance);
}

// "Consume" entry in the game's item context menu (click on an item in the inventory). The button is a
// clone of the menu's own Destroy button, placed right under it, so it shares the menu's look.
[HarmonyPatch(typeof(InventoryContextMenu), nameof(InventoryContextMenu.ShowMenu))]
internal static class DevourerMenuPatch
{
    static UnityEngine.UI.Button _btn;
    static Il2CppTMPro.TextMeshProUGUI _label;
    static InventorySlot _slot;

    static void Postfix(InventoryContextMenu __instance, InventorySlot __0)
    {
        try
        {
            _slot = __0;
            bool show = __0 != null && __0.item != null && __0.quantity > 0 && !__0.isEquipped && Devourer.Active;
            if (_btn == null && show) Create(__instance);
            if (_btn == null) return;
            _btn.gameObject.SetActive(show);
            if (show)
            {
                bool ru = SkillRegistry.Russian();
                var fx = Devourer.BonusFor(__0.item, __0.upgradeLevel);
                string what = string.Join(", ", fx.Select(b => Devourer.ShortLabel(b.stat, b.amount, ru)));
                _label.text = (ru ? "Поглотить" : "Consume") + (what.Length > 0 ? $"  <size=75%>{what}</size>" : "");
                TextFit.Line(_label, _label.fontSize);
            }
            __instance.AdjustContextMenuHeight();
        }
        catch (Exception e) { Plugin.L.LogWarning($"Devourer menu: {e.Message}"); }
    }

    static void Create(InventoryContextMenu menu)
    {
        var src = menu.destroyButton;
        if (src == null) return;
        var go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
        go.name = "FSP_DevourButton";
        go.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
        _btn = go.GetComponent<UnityEngine.UI.Button>();
        _btn.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        _btn.onClick.AddListener((Action)(() =>
        {
            var s = _slot;
            menu.CloseMenu();
            if (s != null && s.item != null) Devourer.Consume(s.item);
        }));
        _label = go.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
        // The clone must not keep the localizer of "Destroy", which would rewrite our label.
        foreach (var c in go.GetComponentsInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true))
            UnityEngine.Object.Destroy(c);
        var img = go.GetComponent<UnityEngine.UI.Image>();
        if (img != null) img.color = new Color(0.55f, 0.35f, 0.85f, 1f);
    }
}
