using System.Collections.Generic;
using HarmonyLib;

namespace FellSellPlus.Inventory;

// Outside the dungeon the game fills empty quick slots before the backpack, so bones and wood
// land there. Routing applies only to pickups and crafting results, never to drag-and-drop or
// equipment moves, which also go through AddItem.
internal static class QuickSlotRouting
{
    public static readonly Dictionary<string, int> PendingPickups = new();
    public static bool Crafting;

    public static bool Consume(Dictionary<string, int> d, string id)
    {
        if (!d.TryGetValue(id, out var n)) return false;
        if (n <= 1) d.Remove(id); else d[id] = n - 1;
        return true;
    }

    public static bool InQuickSlot(ItemData item)
    {
        var q = InventoryManager.Instance?.quickSlots;
        if (q == null) return false;
        foreach (var s in q)
            if (s != null && s.item != null && s.item.name == item.name) return true;
        return false;
    }
}

[HarmonyPatch(typeof(CraftingManager), nameof(CraftingManager.TryCraft))]
internal static class CraftContextPatch
{
    static void Prefix() => QuickSlotRouting.Crafting = true;
    static void Postfix() => QuickSlotRouting.Crafting = false;
}

[HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.AddItem))]
internal static class AddItemPatch
{
    sealed class State { public bool Pickup, Loose; }

    static void Prefix(ItemData item, ref bool forceToMainInventory, out State __state)
    {
        __state = new State();
        if (item == null) return;
        __state.Pickup = QuickSlotRouting.Consume(QuickSlotRouting.PendingPickups, item.name);
        __state.Loose = QuickSlotRouting.Consume(LootFilter.PendingLoose, item.name);
        if (forceToMainInventory || !InventorySettings.QuickSlotRouting.Value) return;
        if (!__state.Pickup && !QuickSlotRouting.Crafting) return;
        if (item.IsStackable && QuickSlotRouting.InQuickSlot(item)) return; // top up the existing stack
        if (item.IsFurniture && InventorySettings.PlaceablesToEmptyQuickSlots.Value) return;
        if (item.itemType == ItemType.Consumable && InventorySettings.ConsumablesToEmptyQuickSlots.Value) return;
        forceToMainInventory = true;
    }

    static void Postfix(ItemData item, int quantity, bool __result, State __state)
    {
        if (__result && __state != null && __state.Loose && item != null) LootFilter.OnPickedUp(item, quantity);
    }
}
