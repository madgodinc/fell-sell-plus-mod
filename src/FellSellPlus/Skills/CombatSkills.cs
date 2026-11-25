using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static FellSellPlus.Skills.SkillRegistry;

namespace FellSellPlus.Skills;

// ---- Fighter: Whirlwind (dash marks the time) ----
[HarmonyPatch(typeof(FirstPersonController), "StartDash")]
internal static class WhirlwindDashPatch
{
    public static float LastDash = -99f;
    static void Postfix() { if (Rank(FighterWhirlwind) > 0) LastDash = Time.time; }
}

// ---- Knight: Steadfast Aura (damage reduction) + Mage: Frost Armor (freeze attacker) ----
// Private core overload PlayerStats.TakeDamage(float, Transform, bool); both public entry points forward to it.
[HarmonyPatch(typeof(PlayerStats), "TakeDamage", new[] { typeof(float), typeof(Transform), typeof(bool) })]
internal static class PlayerHitPatch
{
    static readonly float[] Reduction = { 0f, 0.10f, 0.15f, 0.20f };
    static readonly float[] FreezeChance = { 0f, 0.20f, 0.30f, 0.40f };

    static void Prefix(ref float amount)
    {
        int r = Rank(KnightSteadfast);
        if (r <= 0 || amount <= 0f) return;
        var fpc = FirstPersonController.Instance;
        var input = fpc != null ? fpc.playerInputHandler : null;
        if (input != null && input.IsBlocking) amount *= 1f - Reduction[Math.Min(r, 3)];
    }

    static void Postfix(float amount, Transform attacker)
    {
        int r = Rank(MageFrostArmor);
        if (r <= 0 || amount <= 0f || attacker == null) return;
        if (UnityEngine.Random.value >= FreezeChance[Math.Min(r, 3)]) return;
        var go = PlayerSkillEffectsController.ResolveCombatTarget(attacker.gameObject);
        var fx = SkillUtil.Effects;
        if (go != null && fx != null && SkillUtil.Alive(SkillUtil.HealthOf(go))) fx.TriggerFreezeEnemy(go, 1.5f);
    }
}

// ---- Knight: Retribution (every 10th block -> Smite) ----
[HarmonyPatch(typeof(PlayerSkillEffectsController), nameof(PlayerSkillEffectsController.OnShieldBlockPerformed))]
internal static class RetributionPatch
{
    static int _blocks;

    static void Postfix(PlayerSkillEffectsController __instance, Transform attacker)
    {
        if (Rank(KnightRetribution) <= 0 || ++_blocks % 10 != 0) return;
        var player = SkillUtil.Player;
        var center = attacker != null ? attacker.position : player != null ? player.position : Vector3.zero;
        var combat = SkillUtil.Combat;
        float dmg = combat != null ? combat.GetHighestEquippedWeaponDamage() : 20f;
        __instance.TriggerSmiteImpact(center, dmg, 1f);
    }
}

// ---- Mage: Chain Lightning (every 5th staff projectile) ----
[HarmonyPatch(typeof(PlayerCombat), "SpawnStaffProjectile")]
internal static class ChainLightningPatch
{
    static int _shots;

    static void Postfix(PlayerCombat __instance)
    {
        int r = Rank(MageChainLightning);
        if (r <= 0 || ++_shots % 5 != 0) return;
        var player = SkillUtil.Player;
        var fx = SkillUtil.Effects;
        if (player == null || fx == null) return;
        float dmg = __instance.GetHighestEquippedWeaponDamage() * 0.6f;
        var targets = SkillUtil.EnemiesAround(player.position, 20f);
        for (int i = 0; i < targets.Count && i < r + 1; i++)
            fx.TriggerSmiteImpact(targets[i].transform.position, dmg, 1f);
    }
}

// ---- Occultist: Blood Harvest (cursed enemies explode and spread the curse) ----
[HarmonyPatch(typeof(PlayerSkillEffectsController), nameof(PlayerSkillEffectsController.ApplyCurseToEnemy))]
internal static class CurseTrackPatch
{
    public static readonly Dictionary<int, float> CursedUntil = new();

    static void Postfix(GameObject enemyGo)
    {
        // ApplyCurseToEnemy itself does nothing without the vanilla Curse skill.
        if (enemyGo != null && Rank("occult_curse") > 0) CursedUntil[enemyGo.GetInstanceID()] = Time.time + 5f;
    }
}

[HarmonyPatch(typeof(PlayerSkillEffectsController), "HandleEnemyKilledGlobal")]
internal static class BloodHarvestPatch
{
    static readonly float[] Pct = { 0f, 0.20f, 0.35f, 0.50f };

    static void Postfix(PlayerSkillEffectsController __instance, GameObject enemy)
    {
        int r = Rank(OccultBloodHarvest);
        if (r <= 0 || enemy == null) return;
        int id = enemy.GetInstanceID();
        if (!CurseTrackPatch.CursedUntil.TryGetValue(id, out var until)) return;
        CurseTrackPatch.CursedUntil.Remove(id);
        if (Time.time > until) return;

        var h = enemy.GetComponentInParent<Il2CppEmeraldAI.EmeraldHealth>();
        float maxHp = h != null ? h.StartingHealth : 50f;
        var pos = enemy.transform.position;
        __instance.TriggerArcaneOverloadExplosion(pos, maxHp * Pct[Math.Min(r, 3)], 4.5f);
        foreach (var n in SkillUtil.EnemiesAround(pos, 5f)) __instance.ApplyCurseToEnemy(n.gameObject);
    }
}

// ---- Occultist: Pain Pact (attack speed per 10% missing HP) ----
[HarmonyPatch(typeof(PlayerSkillEffectsController), nameof(PlayerSkillEffectsController.GetAttackSpeedBonusMultiplier))]
internal static class PainPactPatch
{
    static readonly float[] PerStep = { 0f, 0.05f, 0.07f, 0.10f };

    static void Postfix(ref float __result)
    {
        int r = Rank(OccultPainPact);
        var st = PlayerStats.Instance;
        if (r <= 0 || st == null || st.MaxHealth <= 0f) return;
        int steps = Mathf.FloorToInt((1f - st.CurrentHealth / st.MaxHealth) * 10f);
        if (steps > 0) __result *= 1f + steps * PerStep[Math.Min(r, 3)];
    }
}
