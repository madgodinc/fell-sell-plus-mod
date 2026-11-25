using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using C = FellSellPlus.Challenge.ChallengeSettings;

namespace FellSellPlus.Challenge;

// ---------- Forest ----------

// SpawnEnemiesForEvent(config, seed) @1805D8CD0, caller SetupForestScene. The event config is shared data,
// so counts, tough-tier chances and trip damage are set from cached originals each time.
[HarmonyPatch(typeof(ForestEnemySpawner), nameof(ForestEnemySpawner.SpawnEnemiesForEvent))]
internal static class ForestSpawnPatch
{
    static readonly Dictionary<IntPtr, int[]> Counts = new();
    static readonly Dictionary<IntPtr, float> Floats = new();

    static void Prefix(ForestEventManager.ForestEventConfig config)
    {
        if (config == null) return;
        try
        {
            if (!Counts.TryGetValue(config.Pointer, out var o))
                Counts[config.Pointer] = o = new[] { config.minEnemiesLargePoint, config.maxEnemiesLargePoint, config.minEnemiesSmallPoint, config.maxEnemiesSmallPoint };
            float m = C.ForestEnemyCount.Value;
            config.minEnemiesLargePoint = Mathf.RoundToInt(o[0] * m);
            config.maxEnemiesLargePoint = Mathf.Max(config.minEnemiesLargePoint, Mathf.RoundToInt(o[1] * m));
            config.minEnemiesSmallPoint = Mathf.RoundToInt(o[2] * m);
            config.maxEnemiesSmallPoint = Mathf.Max(config.minEnemiesSmallPoint, Mathf.RoundToInt(o[3] * m));

            var tiers = config.enemyTiers;
            if (tiers != null)
                for (int i = 1; i < tiers.Count; i++)   // tier 0 is the regular pool
                {
                    var t = tiers[i];
                    if (t == null) continue;
                    t.baseSpawnChance = Orig(t.Pointer, t.baseSpawnChance) * C.ForestToughEnemies.Value;
                }

            config.damagePerSecond = Orig(config.Pointer, config.damagePerSecond) * C.TripDamage.Value;
            // The spawner reads treasureGoblinSpawnChance from this config (offset 0xA0) during the call.
            config.treasureGoblinSpawnChance = Math.Min(100f, Orig(config.Pointer + 0xA0, config.treasureGoblinSpawnChance) * C.TreasureGoblins.Value);
        }
        catch (Exception e) { Plugin.L.LogWarning($"Forest challenge: {e.Message}"); }
    }

    static float Orig(IntPtr key, float current)
    {
        if (!Floats.TryGetValue(key, out var o)) Floats[key] = o = current;
        return o;
    }
}

// GetPriceModifiersInfo(item) @1804D8D20, callers GetModifiedPrice and GetAdjustedIdealPrice (and the UI).
// The game sums the bonuses: totalMultiplier = 1 + (prestige% + demand% + furniture%) / 100, so the
// furniture part is scaled and the total moved by the same amount.
[HarmonyPatch(typeof(ShopManager), nameof(ShopManager.GetPriceModifiersInfo))]
internal static class FurniturePricePatch
{
    static void Postfix(ref ShopManager.PriceModifiersInfo __result)
    {
        float m = C.FurniturePriceBonus.Value;
        if (Math.Abs(m - 1f) < 0.0001f) return;
        float before = __result.furniturePercent, after = before * m;
        __result.furniturePercent = after;
        __result.totalMultiplier = Math.Max(0.05f, __result.totalMultiplier + (after - before) / 100f);
    }
}

// GetCurrentEventChestSpawnChance @1805DAEB0, caller ForestEventManager.Start.
[HarmonyPatch(typeof(ForestEventManager), nameof(ForestEventManager.GetCurrentEventChestSpawnChance))]
internal static class ForestChestPatch { static void Postfix(ref float __result) => __result *= C.ForestChests.Value; }

// ---------- XP ----------

internal enum XpSource { Other, Sales, Combat, Crafting, Loot }

internal static class XpContext { public static XpSource Source = XpSource.Other; }

// The listener handlers are the callers of AddXP (xref); each one marks the source for its calls.
[HarmonyPatch(typeof(PlayerProgressionEventsListener), "HandleCounterGoldCollected")]
internal static class XpSalesTag { static void Prefix() => XpContext.Source = XpSource.Sales; static void Postfix() => XpContext.Source = XpSource.Other; }

[HarmonyPatch(typeof(PlayerProgressionEventsListener), "HandleEnemyKilled")]
internal static class XpCombatTag { static void Prefix() => XpContext.Source = XpSource.Combat; static void Postfix() => XpContext.Source = XpSource.Other; }

[HarmonyPatch(typeof(PlayerProgressionEventsListener), "HandleItemCrafted")]
internal static class XpCraftTag { static void Prefix() => XpContext.Source = XpSource.Crafting; static void Postfix() => XpContext.Source = XpSource.Other; }

[HarmonyPatch]
internal static class XpLootTag
{
    static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (var n in new[] { "HandleDungeonChestOpened", "HandleBossChestOpened", "HandleCustomChestOpened", "HandleForestChestOpened", "HandleDestructibleShattered" })
        {
            var m = AccessTools.Method(typeof(PlayerProgressionEventsListener), n);
            if (m != null) yield return m;
        }
    }
    static void Prefix() => XpContext.Source = XpSource.Loot;
    static void Postfix() => XpContext.Source = XpSource.Other;
}

// AddXP(int, string) @18046CF60. The XP curve fields are scaled from cached originals here too, since
// GetXPRequiredForLevel / XPToNextLevel are inlined (no callers).
[HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.AddXP))]
internal static class XpGainPatch
{
    static void Prefix(PlayerProgressionManager __instance, ref int amount, string reason)
    {
        XpCurve.Apply(__instance);
        if (amount <= 0) return;
        float m = C.XpAll.Value * XpContext.Source switch
        {
            XpSource.Sales => reason != null && reason.IndexOf("perfect", StringComparison.OrdinalIgnoreCase) >= 0
                ? C.XpPerfectPrice.Value : C.XpSales.Value,
            XpSource.Combat => C.XpCombat.Value,
            XpSource.Crafting => C.XpCrafting.Value,
            XpSource.Loot => C.XpLoot.Value,
            _ => C.XpOther.Value,
        };
        if (Math.Abs(m - 1f) > 0.0001f) amount = Math.Max(0, (int)Math.Round(amount * m));
    }
}

internal static class XpCurve
{
    static int[] _orig;
    static IntPtr _owner;

    public static void Apply(PlayerProgressionManager p)
    {
        if (p == null) return;
        if (_orig == null || _owner != p.Pointer)
        {
            _owner = p.Pointer;
            _orig = new[] { p._baseXPForLevel2, p._xpIncreasePerLevel, p._post5BaseXP, p._post5IncreasePerLevel };
        }
        float m = C.XpToLevel.Value;
        p._baseXPForLevel2 = Math.Max(1, (int)Math.Round(_orig[0] * m));
        p._xpIncreasePerLevel = (int)Math.Round(_orig[1] * m);
        p._post5BaseXP = Math.Max(1, (int)Math.Round(_orig[2] * m));
        p._post5IncreasePerLevel = (int)Math.Round(_orig[3] * m);
    }
}

[HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.RestoreData))]
internal static class XpCurveLoadPatch { static void Prefix(PlayerProgressionManager __instance) => XpCurve.Apply(__instance); }

// ---------- Shop ----------

// GetAdjustedIdealPrice @1804D7E20: customers compare your price with it (callers: customer routine,
// HandleCounterGoldCollected), so scaling it changes what they are willing to pay.
[HarmonyPatch(typeof(ShopManager), nameof(ShopManager.GetAdjustedIdealPrice))]
internal static class SalePricePatch
{
    static void Postfix(ref int __result) => __result = Math.Max(1, (int)Math.Round(__result * C.SalePrices.Value));
}

// GetCurrentShoppingChance @1804D8420, callers: NPC SetupNavMeshAgent, shopping routine.
[HarmonyPatch(typeof(ShopManager), nameof(ShopManager.GetCurrentShoppingChance))]
internal static class ShoppingChancePatch { static void Postfix(ref float __result) => __result *= C.ShoppingChance.Value; }

// PerformRecalculation @1804D9AB0 rebuilds totalTipBonus from the placed furniture each time.
[HarmonyPatch(typeof(ShopManager), "PerformRecalculation")]
internal static class TipsPatch
{
    static void Postfix(ShopManager __instance)
    {
        float m = C.Tips.Value;
        if (Math.Abs(m - 1f) > 0.0001f) __instance.totalTipBonus = (int)Math.Round(__instance.totalTipBonus * m);
    }
}

// Furniture copies: GetDiminishingMultiplier(placedCount) @1804D89D0 (callers: placement multiplier,
// bonus texts, stats text) and its percentage twin @1804D8A10. placedCount is the 0-based copy index (0 -> 100%, 1 -> 50%).
[HarmonyPatch(typeof(ShopManager), nameof(ShopManager.GetDiminishingMultiplier))]
internal static class FurnitureCopiesPatch
{
    static void Postfix(int placedCount, ref float __result) { if (placedCount >= (int)C.MaxPerFurniture.Value) __result = 0f; }
}

[HarmonyPatch(typeof(ShopManager), nameof(ShopManager.GetDiminishingPercentage))]
internal static class FurnitureCopiesTextPatch
{
    static void Postfix(int placedCount, ref int __result) { if (placedCount >= (int)C.MaxPerFurniture.Value) __result = 0; }
}

// Town vendors: ShopSaleItem.GetPrice @1804DF940 (callers: GetActionText, Interact).
[HarmonyPatch(typeof(ShopSaleItem), nameof(ShopSaleItem.GetPrice))]
internal static class VendorPricePatch
{
    static void Postfix(ref int __result) => __result = Math.Max(0, (int)Math.Round(__result * C.VendorPrices.Value));
}

// Interact is reached through the IInteractable interface (no direct calls); the detour still catches it.
[HarmonyPatch(typeof(ShopSaleItem), nameof(ShopSaleItem.Interact))]
internal static class NoVendorBuyingPatch { static bool Prefix() => !C.NoVendorBuying.On; }
