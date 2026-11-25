using System;
using HarmonyLib;
using UnityEngine;

namespace FellSellPlus.Shop;

// Picking up shop furniture: ItemPickup.PickUpItem (0x1807AACC0; callers Interact and the hold-to-pick-up
// coroutine) copies every ShelfSlot's items into a storedItems list and calls
// InventoryManager.AddItem(furniture, 1, ..., storedItems) (0x1807ABFA0). The game keeps those items
// inside the furniture until it is placed again; here they go to the bag first, with their prices.
internal static class FurniturePickup
{
    [ThreadStatic] internal static bool ShelfFurniture;
    [ThreadStatic] internal static bool Moving;
}

[HarmonyPatch(typeof(ItemPickup), "PickUpItem")]
internal static class FurniturePickupContextPatch
{
    static void Prefix(ItemPickup __instance)
    {
        FurniturePickup.ShelfFurniture = false;
        if (__instance != null && __instance.itemData != null && __instance.itemData.IsFurniture)
            CarriedFurniture.PickupUntil = UnityEngine.Time.unscaledTime + 3f;
        if (!ShopSettings.EmptyShelvesOnPickup.Value || __instance == null || __instance.itemData == null || !__instance.itemData.IsFurniture) return;
        // Shelves, tables and racks carry ShelfSlots; chests (their own storage) do not.
        FurniturePickup.ShelfFurniture = __instance.GetComponentsInChildren<ShelfSlot>(true).Length > 0;
    }

    static void Postfix() => FurniturePickup.ShelfFurniture = false;
}

[HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.AddItem))]
internal static class FurnitureStoredItemsPatch
{
    static void Prefix(InventoryManager __instance, ItemData item, Il2CppSystem.Collections.Generic.List<InventorySlot> storedItems)
    {
        if (!FurniturePickup.ShelfFurniture || FurniturePickup.Moving || storedItems == null || storedItems.Count == 0) return;
        if (item == null || !item.IsFurniture) return;
        FurniturePickup.Moving = true;
        int moved = 0, left = 0;
        try
        {
            for (int i = storedItems.Count - 1; i >= 0; i--)
            {
                var s = storedItems[i];
                if (s == null || s.item == null || s.quantity <= 0) continue;
                if (s.storedItems != null && s.storedItems.Count > 0) { left += s.quantity; continue; } // nested containers stay
                int fit = s.quantity;
                while (fit > 0 && !__instance.CanCarryItem(s.item, fit, null)) fit--;
                if (fit > 0 && __instance.AddItem(s.item, fit, true, null, false, s.customPrice, s.upgradeLevel))
                {
                    moved += fit;
                    if (fit >= s.quantity) storedItems.RemoveAt(i);
                    else { s.quantity -= fit; left += s.quantity; }
                }
                else left += s.quantity;
            }
        }
        catch (Exception e) { Plugin.L.LogWarning($"Shelf items to bag: {e.Message}"); }
        finally { FurniturePickup.Moving = false; }
        if (moved > 0 || left > 0)
            ShopModule.Toast(left == 0
                ? Ui.T($"Товары с мебели в сумке: {moved}", $"Items moved to your bag: {moved}")
                : Ui.T($"В сумку: {moved}, не влезло (осталось на мебели): {left}", $"Moved to bag: {moved}, didn't fit (kept on the furniture): {left}"));
    }
}

// Customers ignore slots on furniture that is being picked up (its slots stay live during the
// pick-up animation) or that sits on the placement ghost.
internal static class CarriedFurniture
{
    // Set by the pick-up hook; a pick-up animation lasts about a second.
    public static float PickupUntil = -1f;

    public static bool IsCarried(ShelfSlot slot)
    {
        if (slot == null) return false;
        var fpm = FurniturePlacementManager.Instance;
        var ghost = fpm != null ? fpm.CurrentGhost : null;
        // Nothing is carried or placed: skip the per-slot parent search (customers call this a lot).
        if (ghost == null && UnityEngine.Time.unscaledTime > PickupUntil) return false;
        if (ghost != null && slot.transform.IsChildOf(ghost.transform)) return true;
        var pickup = slot.GetComponentInParent<ItemPickup>();
        return pickup != null && pickup.IsBeingPickedUp;
    }
}

// GetAllAvailableSlots (0x1804D7F40) feeds GetRandomAvailableSlot and NPC_Controller.TryGoShopping.
[HarmonyPatch(typeof(ShopManager), nameof(ShopManager.GetAllAvailableSlots))]
internal static class CarriedSlotsFilterPatch
{
    static void Postfix(Il2CppSystem.Collections.Generic.List<ShelfSlot> __result)
    {
        if (__result == null || !ShopSettings.IgnoreCarriedFurniture.Value) return;
        for (int i = __result.Count - 1; i >= 0; i--)
            if (CarriedFurniture.IsCarried(__result[i])) __result.RemoveAt(i);
    }
}

// A customer already walking to such a slot buys nothing (ProcessPurchase 0x1804DA320, NPC shopping coroutine).
[HarmonyPatch(typeof(ShopManager), nameof(ShopManager.ProcessPurchase))]
internal static class CarriedPurchaseBlockPatch
{
    static bool Prefix(ShelfSlot slot, ref int __result)
    {
        if (!ShopSettings.IgnoreCarriedFurniture.Value || !CarriedFurniture.IsCarried(slot)) return true;
        __result = 0;
        return false;
    }
}

// Furniture placement only accepts positions inside the shop's boundary polygon:
// PathWallCollider.IsInsideAnyActiveBoundary (0x180439760; callers CheckCollisions, UpdateGhostPosition,
// PlaceFurniture). Floor decorations may also go within OutdoorDecorRange of a boundary.
[HarmonyPatch(typeof(PathWallCollider), nameof(PathWallCollider.IsInsideAnyActiveBoundary))]
internal static class OutdoorDecorPatch
{
    static void Postfix(Vector3 worldPos, ref bool __result)
    {
        if (__result || !ShopSettings.OutdoorDecor.Value) return;
        try
        {
            var fpm = FurniturePlacementManager.Instance;
            if (fpm == null || !fpm.IsPlacing) return;
            var item = fpm.CurrentItem;
            if (item == null || item.itemType != ItemType.Furniture_FloorDecoration) return;
            float range = ShopSettings.OutdoorDecorRange.Value;
            var all = PathWallCollider.AllInstances;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || !b.isActiveAndEnabled || !b._polygonBuilt) continue;
                float dx = Mathf.Max(b._minX - worldPos.x, 0f, worldPos.x - b._maxX);
                float dz = Mathf.Max(b._minZ - worldPos.z, 0f, worldPos.z - b._maxZ);
                if (dx * dx + dz * dz <= range * range) { __result = true; return; }
            }
        }
        catch (Exception e) { Plugin.L.LogWarning($"Outdoor decor: {e.Message}"); }
    }
}
