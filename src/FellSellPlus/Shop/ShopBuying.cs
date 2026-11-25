using HarmonyLib;

namespace FellSellPlus.Shop;

// ConstructionManager.BuyShop (0x1804C9D90, called only by ConstructionUI.OnBuyClicked) charges the cost
// and unlocks a new shop, then always starts ConstructionSequence, which moves you in and packs your
// placed items into the construction chest. With BuyShopWithoutMoving a first purchase only charges
// and unlocks; selecting the owned shop again ("Change", cost 0) runs the game's own move.
[HarmonyPatch(typeof(ConstructionManager), nameof(ConstructionManager.BuyShop))]
internal static class BuyWithoutMovingPatch
{
    static bool Prefix(ConstructionManager __instance, ShopData newShop, ref bool __result)
    {
        if (!ShopSettings.BuyShopWithoutMoving.Value || newShop == null || __instance._isConstructing) return true;
        var unlocked = __instance.unlockedShops;
        if (unlocked == null || unlocked.Contains(newShop)) return true; // Change: the game's move-in
        var sm = ShopManager.Instance;
        if (sm == null || sm.PlayerGold < newShop.cost || !sm.SpendGold(newShop.cost)) { __result = false; return false; }
        unlocked.Add(newShop);
        try { QuestStatsTracker.Instance?.RecordBuildingPurchased(); } catch { }
        ShopModule.Toast(Ui.T("Лавка куплена. Чтобы переехать, выберите её и нажмите «Сменить».",
                              "Shop bought. To move in, select it and press Change."));
        __result = true; // the UI closes as after a normal purchase
        return false;
    }
}
