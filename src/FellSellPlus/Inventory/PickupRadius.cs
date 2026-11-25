using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace FellSellPlus.Inventory;

// PlayerAutoPickup scans with OverlapSphereNonAlloc into a 32-entry buffer; walls, props and debris
// fill it first in cluttered rooms and items get skipped. Widen the radius and the buffer once.
[HarmonyPatch(typeof(PlayerAutoPickup), "Awake")]
internal static class PickupRadiusPatch
{
    static void Postfix(PlayerAutoPickup __instance)
    {
        if (__instance == null) return;
        __instance.pickupRadius *= InventorySettings.PickupRadiusMultiplier.Value;
        int size = InventorySettings.PickupScanLimit.Value;
        if (__instance._hitColliders == null || __instance._hitColliders.Length < size)
            __instance._hitColliders = new Il2CppReferenceArray<Collider>(size);
    }
}
