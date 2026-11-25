using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using C = FellSellPlus.Challenge.ChallengeSettings;

namespace FellSellPlus.Challenge;

// DungeonManager.GetEnemyCountMultiplier @180624960, only caller SpawnEnemiesInDungeon.
[HarmonyPatch(typeof(DungeonManager), nameof(DungeonManager.GetEnemyCountMultiplier))]
internal static class EnemyCountPatch { static void Postfix(ref float __result) => __result *= C.EnemyCount.Value; }

// Deeper enemies: SpawnEnemiesInDungeon @1803FDD90 picks eligible enemy groups by
// GetBiomeRelativeFloorLevel(floor) @1803F9C90. That helper is also used elsewhere, so the offset is
// only added while SpawnEnemiesInDungeon runs.
[HarmonyPatch(typeof(CustomDungeonGenerator), "SpawnEnemiesInDungeon")]
internal static class DeeperEnemiesWindow
{
    public static bool Active;
    static void Prefix() => Active = true;
    static void Postfix() { Active = false; ForcedEvil.OnFloorReady(); }
    static Exception Finalizer(Exception __exception) { Active = false; return __exception; }
}

[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.GetBiomeRelativeFloorLevel))]
internal static class DeeperEnemiesPatch
{
    static void Postfix(ref int __result)
    {
        if (DeeperEnemiesWindow.Active) __result += (int)Math.Round(C.DeeperEnemies.Value);
    }
}

// Trap chance: corridorTrapChance lives on the generator and on the active DungeonBiome asset.
// Both are scaled from their cached originals before every generation, so nothing compounds.
[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.GenerateDungeon))]
internal static class TrapChancePatch
{
    static readonly Dictionary<IntPtr, float> Orig = new();

    static void Prefix(CustomDungeonGenerator __instance) => Apply(__instance);

    public static void Apply(CustomDungeonGenerator __instance)
    {
        float m = C.TrapChance.Value;
        Set(__instance.Pointer, __instance.corridorTrapChance, v => __instance.corridorTrapChance = v, m);
        var biome = __instance._activeBiome;
        if (biome != null) Set(biome.Pointer, biome.corridorTrapChance, v => biome.corridorTrapChance = v, m);
    }

    static void Set(IntPtr key, float current, Action<float> write, float m)
    {
        if (!Orig.TryGetValue(key, out var o)) Orig[key] = o = current;
        write(o * m);
    }
}

// BeginGeneration (caller OnSceneLoaded) starts the async build; DetermineActiveBiome picks the biome inside it.
[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.BeginGeneration))]
internal static class TrapChanceAsyncPatch
{
    static void Prefix(CustomDungeonGenerator __instance) => TrapChancePatch.Apply(__instance);
}

[HarmonyPatch(typeof(CustomDungeonGenerator), "DetermineActiveBiome")]
internal static class TrapChanceBiomePatch
{
    static void Postfix(CustomDungeonGenerator __instance) => TrapChancePatch.Apply(__instance);
}

// Dungeon chests carry their roll table as fields. They are scaled for the roll and restored right
// after, so a chest initialised twice never compounds. (DungeonChest.GenerateLoot, caller Initialize.)
[HarmonyPatch(typeof(DungeonChest), "GenerateLoot")]
internal static class ChestLootPatch
{
    sealed class Saved
    {
        public float n, bU, bR, bE, bL, xU, xR, xE, xL, rb, rx;
        public Vector2Int aC, aU, aR, aE, aL;
    }

    static void Prefix(DungeonChest __instance, out Saved __state)
    {
        var c = __instance;
        __state = new Saved
        {
            n = c.nothingWeight, bU = c.baseU, bR = c.baseR, bE = c.baseE, bL = c.baseL,
            xU = c.bonusU, xR = c.bonusR, xE = c.bonusE, xL = c.bonusL,
            rb = c.recipeBaseChance, rx = c.recipeBonusChance,
            aC = c.amtC, aU = c.amtU, aR = c.amtR, aE = c.amtE, aL = c.amtL,
        };
        float rare = C.RareLoot.Value;
        c.nothingWeight *= C.EmptyChest.Value;
        float u = rare * C.UncommonWeight.Value, r = rare * C.RareWeight.Value, e = rare * C.EpicWeight.Value, l = rare * C.LegendaryWeight.Value;
        c.baseU *= u; c.bonusU *= u;
        c.baseR *= r; c.bonusR *= r;
        c.baseE *= e; c.bonusE *= e;
        c.baseL *= l; c.bonusL *= l;
        float a = C.ItemAmount.Value;
        c.amtC = Scale(c.amtC, a); c.amtU = Scale(c.amtU, a); c.amtR = Scale(c.amtR, a);
        c.amtE = Scale(c.amtE, a); c.amtL = Scale(c.amtL, a);
        float rc = rare * C.RecipeChance.Value;
        c.recipeBaseChance *= rc;
        c.recipeBonusChance *= rc;
    }

    static void Postfix(DungeonChest __instance, Saved __state)
    {
        var c = __instance; var s = __state;
        c.nothingWeight = s.n; c.baseU = s.bU; c.baseR = s.bR; c.baseE = s.bE; c.baseL = s.bL;
        c.bonusU = s.xU; c.bonusR = s.xR; c.bonusE = s.xE; c.bonusL = s.xL;
        c.recipeBaseChance = s.rb; c.recipeBonusChance = s.rx;
        c.amtC = s.aC; c.amtU = s.aU; c.amtR = s.aR; c.amtE = s.aE; c.amtL = s.aL;
    }

    static Vector2Int Scale(Vector2Int v, float m) =>
        Math.Abs(m - 1f) < 0.0001f ? v : new(Math.Max(1, Mathf.RoundToInt(v.x * m)), Math.Max(1, Mathf.RoundToInt(v.y * m)));
}

// GetDropChanceBonus @18049A840, callers: GenerateLoot (x4).
[HarmonyPatch(typeof(PrayersManager), nameof(PrayersManager.GetDropChanceBonus))]
internal static class DropChancePatch { static void Postfix(ref float __result) => __result += C.ExtraDropChance.Value; }
