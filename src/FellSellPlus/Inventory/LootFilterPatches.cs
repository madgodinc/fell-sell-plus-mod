using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FellSellPlus.Inventory;

// Decides what the loot rules say about one loose item in the dungeon/forest.
internal static class LootFilter
{
    // Picked up since leaving town, per item id.
    public static readonly Dictionary<string, int> RunCounts = new();
    // Items whose PickUpItem started for a loose dungeon/forest pickup; AddItem runs later (animation).
    public static readonly Dictionary<string, int> PendingLoose = new();
    static readonly HashSet<string> Alerted = new();
    public static bool AutoContext;

    public static void ResetRun() { RunCounts.Clear(); Alerted.Clear(); PendingLoose.Clear(); }

    // null = no opinion (Default rule or not applicable).
    public static bool? Allows(ItemData item)
    {
        if (!InventorySettings.FilterEnabled.Value || item == null || !ItemCounts.InRun()) return null;
        var r = LootRules.Get(item.name);
        if (r == null) return null;
        switch (r.Rule)
        {
            case RuleKind.Never: return false;
            case RuleKind.Always: return true;
            case RuleKind.Total: return ItemCounts.Have(item.name) < r.Limit;
            case RuleKind.ThisRun: return RunCounts.GetValueOrDefault(item.name) < r.Limit;
            case RuleKind.Need: return NeedList.Missing(item.name) > 0;
            default: return null;
        }
    }

    public static void OnPickedUp(ItemData item, int qty)
    {
        RunCounts[item.name] = RunCounts.GetValueOrDefault(item.name) + qty;
        var r = LootRules.Get(item.name);
        if (r == null || !r.Alert || !InventorySettings.Alerts.Value) return;
        bool reached = r.Rule switch
        {
            RuleKind.Total => ItemCounts.Have(item.name) >= r.Limit,
            RuleKind.ThisRun => RunCounts[item.name] >= r.Limit,
            RuleKind.Need => NeedList.Missing(item.name) == 0,
            _ => false,
        };
        if (!reached) { Alerted.Remove(item.name); return; }
        if (!Alerted.Add(item.name)) return;
        string what = r.Rule switch
        {
            RuleKind.Total => Ru ? $"{r.Limit} набрано (всего)" : $"{r.Limit} reached (total)",
            RuleKind.ThisRun => Ru ? $"{r.Limit} набрано (за забег)" : $"{r.Limit} reached (this run)",
            _ => Ru ? "хватает для рецептов" : "recipes covered",
        };
        try
        {
            PickupNotificationManager.Instance?.NotifyCustom(item.icon, $"{item.GetDisplayName()}: {what}", new Color(0.45f, 1f, 0.45f), 1);
            ItemPickup.PlayPickupSoundWithCombo(Il2CppSmallHedge.SoundManager.SoundType.Item_Pickup);
        }
        catch { }
    }

    static bool Ru => Progression.GameQuery.IsRussian();
}

// The game's group/rarity Loot options; a rule other than Default replaces them for that item.
[HarmonyPatch(typeof(OptionsManager), nameof(OptionsManager.CanAutoPickupItem))]
internal static class AutoPickupRulePatch
{
    static void Postfix(ItemPickup pickup, ref bool __result)
    {
        var verdict = LootFilter.Allows(pickup?.itemData);
        if (verdict.HasValue) __result = verdict.Value;
    }
}

// Marks pickups started by the proximity scan, so the manual-E check below leaves them alone.
[HarmonyPatch(typeof(PlayerAutoPickup), "TryPickup")]
internal static class AutoPickupContextPatch
{
    static void Prefix() => LootFilter.AutoContext = true;
    static void Postfix() => LootFilter.AutoContext = false;
}

// E on a filtered item does nothing when the rule has Manual = false.
[HarmonyPatch(typeof(ItemPickup), nameof(ItemPickup.Interact))]
internal static class ManualPickupPatch
{
    static bool Prefix(ItemPickup __instance) => Allowed(__instance);

    public static bool Allowed(ItemPickup __instance)
    {
        if (LootFilter.AutoContext || __instance == null || !__instance.IsLooseInScene()) return true;
        var r = LootRules.Get(__instance.itemData?.name);
        if (r == null || r.Manual) return true;
        return LootFilter.Allows(__instance.itemData) != false;
    }
}

// Heavy items use hold-to-pick-up, which reaches PickUpItem without Interact.
[HarmonyPatch(typeof(ItemPickup), nameof(ItemPickup.StartHoldInteract))]
internal static class ManualHoldPickupPatch
{
    static bool Prefix(ItemPickup __instance) => ManualPickupPatch.Allowed(__instance);
}

[HarmonyPatch(typeof(ItemPickup), nameof(ItemPickup.GetActionText))]
internal static class FilteredPromptPatch
{
    static void Postfix(ItemPickup __instance, ref string __result)
    {
        if (__instance == null || !__instance.IsLooseInScene()) return;
        var r = LootRules.Get(__instance.itemData?.name);
        if (r == null || r.Manual || LootFilter.Allows(__instance.itemData) != false) return;
        __result = Progression.GameQuery.IsRussian() ? "Отфильтровано (Fell & Sell Plus)" : "Filtered (Fell & Sell Plus)";
    }
}

// Remember loose dungeon/forest pickups; the item reaches the inventory through AddItem after the animation.
[HarmonyPatch(typeof(ItemPickup), "PickUpItem")]
internal static class PickupStartPatch
{
    static void Prefix(ItemPickup __instance)
    {
        var item = __instance?.itemData;
        if (item == null) return;
        if (ItemCounts.InRun() && __instance.IsLooseInScene())
            LootFilter.PendingLoose[item.name] = LootFilter.PendingLoose.GetValueOrDefault(item.name) + 1;
        QuickSlotRouting.PendingPickups[item.name] = QuickSlotRouting.PendingPickups.GetValueOrDefault(item.name) + 1;
    }
}

// Dropped items come off the run count, so picking them back up does not count twice.
[HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.DropItem))]
internal static class RunDropPatch
{
    static void Prefix(InventorySlot slot, int amount)
    {
        if (slot?.item == null || !ItemCounts.InRun()) return;
        var id = slot.item.name;
        if (LootFilter.RunCounts.TryGetValue(id, out var n))
            LootFilter.RunCounts[id] = Mathf.Max(0, n - Mathf.Max(0, amount));
    }
}
