using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using static FellSellPlus.Skills.SkillRegistry;

namespace FellSellPlus.Skills;

// Second wave of skills. Hooks (callers checked with xref on the compiled game):
//  PlayerStats.TakeDamage(float, Transform, bool) 0x180490AF0  <- TakeDamage, TakeBombDamage, DebugTakeDamage
//  PlayerStats.Die                                0x18048BB60  <- TakeDamage, Update (HP drain), CurrentHealth setter
//  PlayerStats.RecalculateAllStats                0x18048E2A0  <- Start, RestoreData, RefreshStatsForScene, ReduceMaxMana...
//  PlayerStats.Update                             0x180492E60  (Unity message)
//  PlayerStats.TryConsumeStamina(float, out bool) 0x180492660  <- TakeDamage (block), attacks, jump
//  FirstPersonController.CurrentSpeed getter      0x1804310E0  <- HandleMovement x2
//  FirstPersonController.StartDash                0x18042D310  <- OnDashPressed / double tap
//  PlayerSkillEffectsController.OnShieldBlockPerformed 0x180473390 <- PlayerStats.TakeDamage
//  PlayerSkillEffectsController.HandleEnemyKilledGlobal 0x1804703F0 (event handler; body hook)
//  PlayerSkillEffectsController.GetAttackSpeedBonusMultiplier 0x18046FE60 <- PlayerStats
//  PlayerCombat.SpawnStaffProjectile              0x18044FE20  <- attack coroutine
//  DifficultyManager.OutgoingDamageMultiplier     0x1805C2900  <- CalculateDamage
// Fields raised around a game method are always added and subtracted (never multiplied), so they
// stack correctly with the progression and challenge hooks on the same methods.

internal static class SkillState
{
    public static float LastDash = -99f, LastBlock = -99f, LastKill = -99f;
    public static float InvulnerableUntil = -99f;

    // Per-floor bookkeeping: a "floor" is the scene plus the dungeon floor number.
    static string _floorKey = "";
    public static int FloorKills;
    public static bool BulwarkUsed, TimeLoopUsed;

    public static void CheckFloor()
    {
        string scene = SceneManager.GetActiveScene().name;
        int floor = scene == "DUNGEON" && DungeonManager.Instance != null ? DungeonManager.Instance.currentFloor : 0;
        string key = scene + "#" + floor;
        if (key == _floorKey) return;
        _floorKey = key;
        FloorKills = 0; BulwarkUsed = false; TimeLoopUsed = false;
        SoulSiphonPools.Stacks = 0;
        if (PlayerStats.Instance != null) Progression.GameQuery.RefreshPlayerStats();
    }

    public static float MaxHp => PlayerStats.Instance != null ? PlayerStats.Instance.MaxHealth : 100f;
}

// ---------------- shared event taps ----------------

[HarmonyPatch(typeof(FirstPersonController), "StartDash")]
internal static class DashTapPatch
{
    static void Postfix()
    {
        SkillState.LastDash = Time.time;
        if (Rank(FighterUnstoppable) > 0) SkillState.InvulnerableUntil = Math.Max(SkillState.InvulnerableUntil, Time.time + 1f);
    }
}

[HarmonyPatch(typeof(PlayerSkillEffectsController), nameof(PlayerSkillEffectsController.OnShieldBlockPerformed))]
internal static class BlockTapPatch
{
    static void Postfix() => SkillState.LastBlock = Time.time;
}

// ---------------- Fighter ----------------

// Momentum: movement speed after a dash.
[HarmonyPatch(typeof(FirstPersonController), "CurrentSpeed", MethodType.Getter)]
internal static class MomentumPatch
{
    static readonly float[] Bonus = { 0f, 0.10f, 0.15f, 0.20f };
    static void Postfix(ref float __result)
    {
        if (Time.time - SkillState.LastDash > 3f) return;
        int r = Rank(FighterMomentum);
        if (r > 0) __result *= 1f + Bonus[Math.Min(r, 3)];
    }
}

// Kill effects: Bloodthirst, Steel Storm, Soul Siphon, Lord of Death, Harvest Rite.
[HarmonyPatch(typeof(PlayerSkillEffectsController), "HandleEnemyKilledGlobal")]
internal static class KillSkillsPatch
{
    static readonly float[] HealPct = { 0f, 0.03f, 0.05f, 0.08f };
    static bool _exploding;

    static void Postfix(PlayerSkillEffectsController __instance, GameObject enemy)
    {
        SkillState.CheckFloor();
        SkillState.LastKill = Time.time;
        SkillState.FloorKills++;
        var ps = PlayerStats.Instance;

        int r = Rank(FighterBloodthirst);
        if (r > 0 && ps != null) ps.Heal(ps.MaxHealth * HealPct[Math.Min(r, 3)]);

        if (Rank(OccultSoulSiphon) > 0 && SoulSiphonPools.Stacks < 20)
        {
            SoulSiphonPools.Stacks++;
            Progression.GameQuery.RefreshPlayerStats();
        }

        // Explosions can kill more enemies; their kills must not chain explosions forever.
        if (Rank(OccultLordOfDeath) > 0 && enemy != null && !_exploding)
        {
            var h = enemy.GetComponentInParent<Il2CppEmeraldAI.EmeraldHealth>();
            float maxHp = h != null ? h.StartingHealth : 50f;
            _exploding = true;
            try { __instance.TriggerArcaneOverloadExplosion(enemy.transform.position, maxHp * 0.30f, 4f); }
            finally { _exploding = false; }
        }
    }
}

[HarmonyPatch(typeof(PlayerSkillEffectsController), nameof(PlayerSkillEffectsController.GetAttackSpeedBonusMultiplier))]
internal static class SteelStormPatch
{
    static void Postfix(ref float __result)
    {
        if (Time.time - SkillState.LastKill <= 5f && Rank(FighterSteelStorm) > 0) __result *= 1.30f;
    }
}

// Invulnerability windows (Unstoppable after a dash, Bulwark after a saved death) and Mana Shield.
[HarmonyPatch(typeof(PlayerStats), "TakeDamage", new[] { typeof(float), typeof(Transform), typeof(bool) })]
internal static class InvulnerablePatch
{
    static bool Prefix(ref float __result)
    {
        if (Time.time >= SkillState.InvulnerableUntil) return true;
        __result = 0f;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerStats), "TakeDamage", new[] { typeof(float), typeof(Transform), typeof(bool) })]
internal static class ManaShieldPatch
{
    static readonly float[] Share = { 0f, 0.30f, 0.40f, 0.50f };
    static void Prefix(PlayerStats __instance, ref float amount)
    {
        int r = Rank(MageManaShield);
        if (r <= 0 || amount <= 0f) return;
        float absorb = Math.Min(amount * Share[Math.Min(r, 3)], __instance.CurrentMana);
        if (absorb <= 0f) return;
        __instance.CurrentMana = __instance.CurrentMana - absorb;
        amount -= absorb;
    }
}

// ---------------- Knight ----------------

[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.TryConsumeStamina), new[] { typeof(float), typeof(bool) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
internal static class ShieldbearerPatch
{
    static readonly float[] Cut = { 0f, 0.15f, 0.25f, 0.35f };
    static void Prefix(ref float amount)
    {
        int r = Rank(KnightShieldbearer);
        if (r <= 0 || amount <= 0f) return;
        var input = FirstPersonController.Instance != null ? FirstPersonController.Instance.playerInputHandler : null;
        if (input != null && input.IsBlocking) amount *= 1f - Cut[Math.Min(r, 3)];
    }
}

// Rot: damage over time started by the on-hit hook (HitSkills.cs).

internal static class RotTicker
{
    sealed class Dot { public Il2CppEmeraldAI.EmeraldHealth H; public float Pct, Until, Next; }
    static readonly Dictionary<IntPtr, Dot> Dots = new();

    public static void Apply(Il2CppEmeraldAI.EmeraldHealth h, float pct)
    {
        if (!SkillUtil.Alive(h)) return;
        if (Dots.TryGetValue(h.Pointer, out var d)) { d.Until = Time.time + 4f; return; } // refresh
        Dots[h.Pointer] = new Dot { H = h, Pct = pct, Until = Time.time + 4f, Next = Time.time + 1f };
    }

    public static void Tick()
    {
        if (Dots.Count == 0) return;
        var player = SkillUtil.Player;
        var done = new List<IntPtr>();
        foreach (var (key, d) in Dots)
        {
            if (!SkillUtil.Alive(d.H) || Time.time > d.Until) { done.Add(key); continue; }
            if (Time.time < d.Next) continue;
            d.Next += 1f;
            int dmg = Math.Max(1, Mathf.RoundToInt(d.H.StartingHealth * d.Pct));
            d.H.Damage(dmg, player, 0, false);
        }
        foreach (var k in done) Dots.Remove(k);
    }
}

// Bulwark and Time Loop: PlayerStats.Die is reached only for a real death (health already 0;
// the game's own death saves are handled before it). Die sets _isDead itself, so skipping it
// leaves the player cleanly alive.
[HarmonyPatch(typeof(PlayerStats), "Die")]
internal static class CheatDeathPatch
{
    static bool Prefix(PlayerStats __instance)
    {
        SkillState.CheckFloor();
        if (Rank(MageTimeLoop) > 0 && TimeLoop.TryRewind())
        {
            SkillState.InvulnerableUntil = Time.time + 30f; // until the reload replaces this scene
            return false;
        }
        if (!SkillState.BulwarkUsed && Rank(KnightBulwark) > 0)
        {
            SkillState.BulwarkUsed = true;
            __instance.CurrentHealth = 1f;
            SkillState.InvulnerableUntil = Time.time + 2f;
            Plugin.L.LogInfo("Bulwark saved the player.");
            return false;
        }
        return true;
    }
}

// ---------------- pools and regen (Sturdy, Focus, Soul Siphon, Mana Font) ----------------

[HarmonyPatch(typeof(PlayerStats), "RecalculateAllStats")]
internal static class SoulSiphonPools
{
    public static int Stacks;
    static readonly float[] SiphonPer = { 0f, 2f, 3f, 4f };
    static readonly float[] SturdyHp = { 0f, 10f, 20f, 30f };
    static readonly float[] FocusMana = { 0f, 10f, 20f, 30f };
    static int _depth;
    static float _hp, _mana;

    static void Prefix(PlayerStats __instance)
    {
        if (_depth++ != 0) return;
        int s = Rank(OccultSoulSiphon), st = Rank(KnightSturdy), f = Rank(MageFocus);
        _hp = SturdyHp[Math.Min(st, 3)] + (s > 0 ? Stacks * SiphonPer[Math.Min(s, 3)] : 0f);
        _mana = FocusMana[Math.Min(f, 3)];
        __instance._baseMaxHealth += _hp;
        __instance._baseMaxMana += _mana;
    }

    static void Postfix(PlayerStats __instance)
    {
        if (--_depth != 0) return;
        __instance._baseMaxHealth -= _hp;
        __instance._baseMaxMana -= _mana;
    }
}

[HarmonyPatch(typeof(PlayerStats), "Update")]
internal static class ManaFontAndTicksPatch
{
    static readonly float[] Bonus = { 0f, 0.25f, 0.50f, 0.75f };
    static int _depth;
    static float _delta, _nextRankCheck;
    static int _lastPoolRanks = -1;

    static void Prefix(PlayerStats __instance)
    {
        if (_depth++ != 0) return;
        _delta = 0f;
        int r = Rank(MageManaFont);
        if (r > 0 && !__instance.IsInCombat)
        {
            _delta = __instance._manaRegenRate * Bonus[Math.Min(r, 3)];
            __instance._manaRegenRate += _delta;
        }
    }

    static void Postfix(PlayerStats __instance)
    {
        if (--_depth != 0) return;
        __instance._manaRegenRate -= _delta;
        RotTicker.Tick();

        // Max-pool skills change when a rank is bought: refresh the player's pools then.
        if (Time.unscaledTime < _nextRankCheck) return;
        _nextRankCheck = Time.unscaledTime + 1f;
        SkillState.CheckFloor();
        int ranks = Rank(KnightSturdy) * 100 + Rank(MageFocus) * 10 + Rank(OccultSoulSiphon);
        if (ranks != _lastPoolRanks)
        {
            bool first = _lastPoolRanks < 0;
            _lastPoolRanks = ranks;
            if (!first) Progression.GameQuery.RefreshPlayerStats();
        }
    }
}

// ---------------- Mage ----------------

[HarmonyPatch(typeof(PlayerCombat), "SpawnStaffProjectile")]
internal static class OverloadPatch
{
    static int _shots;
    static void Postfix(PlayerCombat __instance)
    {
        if (Rank(MageOverload) <= 0 || ++_shots % 4 != 0) return;
        var player = SkillUtil.Player;
        var fx = SkillUtil.Effects;
        if (player == null || fx == null) return;
        var targets = SkillUtil.EnemiesAround(player.position, 20f);
        if (targets.Count == 0) return;
        fx.TriggerArcaneOverloadExplosion(targets[0].transform.position, __instance.GetHighestEquippedWeaponDamage() * 2f, 4f);
    }
}

// ---------------- Occultist: Harvest Rite (all outgoing damage) ----------------

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.OutgoingDamageMultiplier), MethodType.Getter)]
internal static class HarvestRitePatch
{
    static void Postfix(ref float __result)
    {
        if (Rank(OccultHarvestRite) <= 0) return;
        SkillState.CheckFloor();
        int steps = Math.Min(5, SkillState.FloorKills / 10);
        if (steps > 0) __result *= 1f + 0.10f * steps;
    }
}
