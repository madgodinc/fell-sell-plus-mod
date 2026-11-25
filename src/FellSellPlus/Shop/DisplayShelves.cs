using HarmonyLib;
using UnityEngine;

namespace FellSellPlus.Shop;

// Display shelves: slots the player marks (hold U) are not offered to customers.
// GetAllAvailableSlots (0x1804D7F40) is the only source of candidate slots: it is called by
// GetRandomAvailableSlot and NPC_Controller.TryGoShopping.
[HarmonyPatch(typeof(ShopManager), nameof(ShopManager.GetAllAvailableSlots))]
internal static class DisplaySlotsFilterPatch
{
    static void Postfix(Il2CppSystem.Collections.Generic.List<ShelfSlot> __result)
    {
        if (__result == null || !ShopSettings.DisplayShelves.Value) return;
        for (int i = __result.Count - 1; i >= 0; i--)
        {
            var s = __result[i];
            if (s != null && ShopStore.IsDisplay(s.transform.position)) __result.RemoveAt(i);
        }
    }
}

// A customer already walking to a slot that became a display buys nothing from it.
// ProcessPurchase (0x1804DA320) is called from the NPC shopping coroutine.
[HarmonyPatch(typeof(ShopManager), nameof(ShopManager.ProcessPurchase))]
internal static class DisplayPurchaseBlockPatch
{
    static bool Prefix(ShelfSlot slot, ref int __result)
    {
        if (!ShopSettings.DisplayShelves.Value || slot == null || !ShopStore.IsDisplay(slot.transform.position)) return true;
        __result = 0;
        return false;
    }
}

// Price tag reads "Display" instead of the price.
[HarmonyPatch(typeof(ShelfSlot), nameof(ShelfSlot.UpdateSlotUI))]
internal static class DisplayPriceTagPatch
{
    static void Postfix(ShelfSlot __instance)
    {
        if (!ShopSettings.DisplayShelves.Value || __instance == null || __instance.priceText == null) return;
        if (!__instance.IsEmpty && ShopStore.IsDisplay(__instance.transform.position))
            __instance.priceText.text = Ui.T("Витрина", "Display");
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteSlotData))]
internal static class ShopStoreDeletePatch
{
    static void Postfix(int slot) => ShopStore.DeleteSlot(slot);
}

internal static class DisplayShelves
{
    public static void Toggle(ShelfSlot slot)
    {
        bool on = ShopStore.ToggleDisplay(slot.transform.position);
        slot.UpdateSlotUI();
        ShopModule.Toast(on ? Ui.T("Полка стала витриной", "Shelf is now a display")
                            : Ui.T("Полка снова продаёт", "Shelf is for sale again"));
    }
}
