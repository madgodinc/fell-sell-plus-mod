using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppEmeraldAI;
using UnityEngine;
using C = FellSellPlus.Challenge.ChallengeSettings;

namespace FellSellPlus.Challenge;

// DifficultyManager getters are real calls (xref): EnemyHealth <- EmeraldHealth Start, Incoming <- PlayerStats.TakeDamage,
// Outgoing <- CalculateDamage, Speed <- AI movement, Cooldown <- GenerateRandomCooldown, Lunge <- TriggerAttackLunge,
// Combo <- ExecuteAttack, KeepEquipped <- death routine, Furniture <- PerformRecalculation / stats text.

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.EnemyHealthMultiplier), MethodType.Getter)]
internal static class EnemyHealthPatch { static void Postfix(ref float __result) => __result *= C.EnemyHealth.Value; }

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.IncomingDamageMultiplier), MethodType.Getter)]
internal static class EnemyDamagePatch { static void Postfix(ref float __result) => __result *= C.EnemyDamage.Value; }

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.OutgoingDamageMultiplier), MethodType.Getter)]
internal static class PlayerDamagePatch { static void Postfix(ref float __result) => __result *= C.PlayerDamage.Value; }

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.EnemySpeedMultiplier), MethodType.Getter)]
internal static class EnemySpeedPatch { static void Postfix(ref float __result) => __result *= C.EnemySpeed.Value; }

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.EnemyAttackCooldownMultiplier), MethodType.Getter)]
internal static class EnemyAttackRatePatch { static void Postfix(ref float __result) => __result /= Math.Max(0.01f, C.EnemyAttackRate.Value); }

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.CanEnemyLunge), MethodType.Getter)]
internal static class EnemyLungePatch { static void Postfix(ref bool __result) { if (C.EnemiesLungeAndCombo.On) __result = true; } }

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.CanEnemyCombo), MethodType.Getter)]
internal static class EnemyComboPatch { static void Postfix(ref bool __result) { if (C.EnemiesLungeAndCombo.On) __result = true; } }

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.KeepEquippedItemsOnDeath), MethodType.Getter)]
internal static class LoseGearPatch { static void Postfix(ref bool __result) { if (C.LoseGearOnDeath.On) __result = false; } }

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.FurnitureCombatBonusMultiplier), MethodType.Getter)]
internal static class FurnitureCombatPatch { static void Postfix(ref float __result) => __result *= C.FurnitureCombatBonus.Value; }

// Private TakeDamage(float, Transform, bool) @180490AF0: both public overloads go through it.
// ArmorKFactor's getter is inlined, so the field is scaled for the duration of the call:
// damage = K/(K+armor); armor counting x0.7 equals K/0.7.
[HarmonyPatch(typeof(PlayerStats), "TakeDamage", typeof(float), typeof(Transform), typeof(bool))]
internal static class PlayerTakeDamagePatch
{
    static void Prefix(PlayerStats __instance, ref float amount, Transform attacker, out float __state)
    {
        __state = __instance._armorKFactor;
        float eff = C.ArmorEffect.Value;
        if (eff > 0.001f && Math.Abs(eff - 1f) > 0.0001f) __instance._armorKFactor = __state / eff;
        if (attacker != null && Bosses.IsBoss(attacker)) amount *= C.BossDamage.Value;
    }

    static void Postfix(PlayerStats __instance, float __state) => __instance._armorKFactor = __state;
}

// Stamina: discrete costs (jump, attacks, dodge via the 1-arg overload, blocked hits) go through
// TryConsumeStamina(float, out bool) @180492660; sprint and held block drain inside Update from fields.
[HarmonyPatch]
internal static class StaminaCostPatch
{
    static System.Reflection.MethodBase TargetMethod() =>
        AccessTools.Method(typeof(PlayerStats), nameof(PlayerStats.TryConsumeStamina), new[] { typeof(float), typeof(bool).MakeByRefType() });

    static void Prefix(ref float amount) => amount *= C.StaminaDrain.Value;
}

[HarmonyPatch(typeof(PlayerStats), "Update")]
internal static class StaminaDrainPatch
{
    static void Prefix(PlayerStats __instance, out (float sprint, float block) __state)
    {
        __state = (__instance._staminaDrainSprint, __instance._staminaDrainBlock);
        float m = C.StaminaDrain.Value;
        __instance._staminaDrainSprint *= m;
        __instance._staminaDrainBlock *= m;
    }

    static void Postfix(PlayerStats __instance, (float sprint, float block) __state)
    {
        __instance._staminaDrainSprint = __state.sprint;
        __instance._staminaDrainBlock = __state.block;
    }
}

// Bosses: BossRoomSpawner.SpawnEnemiesNow instantiates them; its scaling helpers only run on some paths,
// so the new EmeraldHealth objects that appear during the call are taken as the bosses.
internal static class Bosses
{
    static readonly HashSet<IntPtr> Ptrs = new();
    public static HashSet<IntPtr> Snapshot()
    {
        var set = new HashSet<IntPtr>();
        foreach (var h in UnityEngine.Object.FindObjectsOfType<EmeraldHealth>()) set.Add(h.Pointer);
        return set;
    }
    public static void Mark(HashSet<IntPtr> before)
    {
        float m = C.BossHealth.Value;
        foreach (var h in UnityEngine.Object.FindObjectsOfType<EmeraldHealth>())
        {
            if (before.Contains(h.Pointer)) continue;
            Ptrs.Add(h.gameObject.Pointer);
            if (Math.Abs(m - 1f) > 0.0001f)
            {
                h.StartingHealth = Math.Max(1, Mathf.RoundToInt(h.StartingHealth * m));
                h.CurrentHealth = h.StartingHealth;
            }
        }
    }
    public static bool IsBoss(Transform t)
    {
        var h = t.GetComponentInParent<EmeraldHealth>();
        return h != null && Ptrs.Contains(h.gameObject.Pointer);
    }
}

[HarmonyPatch(typeof(BossRoomSpawner), nameof(BossRoomSpawner.SpawnEnemiesNow))]
internal static class BossSpawnPatch
{
    static void Prefix(out HashSet<IntPtr> __state) => __state = Bosses.Snapshot();
    static void Postfix(HashSet<IntPtr> __state) { try { Bosses.Mark(__state); } catch (Exception e) { Plugin.L.LogWarning($"Boss scaling: {e.Message}"); } }
}

// Extra elites: ApplyDungeonPlusAndEliteChance (callers: SpawnEnemiesInDungeon, SpawnWaveEnemies,
// SpawnEnemiesAndForceAggro, ActivateEnemiesAndForceAggro) is where the game itself adds EliteEnemyController
// and calls InitializeElite; the mod does the same for an extra roll.
[HarmonyPatch(typeof(DungeonPlusManager), nameof(DungeonPlusManager.ApplyDungeonPlusAndEliteChance))]
internal static class ExtraElitePatch
{
    static readonly System.Random Rng = new();

    static void Postfix(GameObject enemyInstance)
    {
        float chance = C.ExtraEliteChance.Value;
        if (chance <= 0f || enemyInstance == null) return;
        int tier = DungeonPlusManager.Instance != null ? DungeonPlusManager.Instance.ActiveTier : 0;
        if (tier < 1 && !C.ElitesOutsideDungeonPlus.On) return;
        if (enemyInstance.GetComponent<EliteEnemyController>() != null) return;
        if (Rng.NextDouble() >= chance) return;

        var values = (EliteAffix[])Enum.GetValues(typeof(EliteAffix));
        var main = values[Rng.Next(values.Length)];
        var second = new Il2CppSystem.Nullable<EliteAffix>();
        if (Rng.NextDouble() < C.EliteSecondAffix.Value)
        {
            var s = values[Rng.Next(values.Length)];
            if (s != main) second = new Il2CppSystem.Nullable<EliteAffix>(s);
        }
        bool plus = Rng.NextDouble() < C.ElitePlusChance.Value;
        var elite = enemyInstance.AddComponent<EliteEnemyController>();
        elite.InitializeElite(main, second, plus);
    }
}
