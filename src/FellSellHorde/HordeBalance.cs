using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppEmeraldAI;
using UnityEngine;

namespace FellSellHorde;

// How strong enemies are. Two layers:
// 1. The table: health and damage grow slowly up to wave 30, then compound, faster after 70, so
//    late waves keep up with a player whose gifts stack without limit.
// 2. From AdaptFromWave, a floor tied to the player's real power: the last 30 hits the player landed
//    set how much health an enemy needs to take a target number of hits, and the player's max
//    health sets how hard an enemy hit must land. The floor only ever raises, never lowers.
internal static class HordeBalance
{
    internal enum Kind { Normal, Elite, Boss, Grandma }

    const int AdaptFromWave = 30;

    // Three stages (Mad): waves 1-30 barely noticeable, 30-70 clearly harder, 70+ very hard.
    // The difficulty preset scales every rate (Normal = 6% health / 3% damage per wave = 1.0).
    internal static float HealthMul(int w)
    {
        float k = HordeSettings.HealthPercentPerWave.Value / 6f;
        float m = 1f + 0.02f * k * (Math.Min(w, 30) - 1);                       // wave 30: x1.58
        if (w > 30) m *= Mathf.Pow(1f + 0.04f * k, Math.Min(w, 70) - 30);       // wave 70: x7.6
        if (w > 70) m *= Mathf.Pow(1f + 0.07f * k, w - 70);                     // wave 100: x58
        return m * (1f + HordeSettings.HealthPercentPerLevel.Value / 100f * HordeProgress.Level);
    }

    internal static float DamageMul(int w)
    {
        float k = HordeSettings.DamagePercentPerWave.Value / 3f;
        float m = 1f + 0.01f * k * (Math.Min(w, 30) - 1);                       // wave 30: x1.29
        if (w > 30) m *= Mathf.Pow(1f + 0.025f * k, Math.Min(w, 70) - 30);      // wave 70: x3.5
        if (w > 70) m *= Mathf.Pow(1f + 0.045f * k, w - 70);                    // wave 100: x13
        return m * (1f + HordeSettings.DamagePercentPerLevel.Value / 100f * HordeProgress.Level);
    }

    // ---------- the player's real hit ----------

    static readonly Queue<int> Hits = new();

    internal static void RecordHit(int dmg)
    {
        if (dmg <= 0) return;
        Hits.Enqueue(dmg);
        while (Hits.Count > 30) Hits.Dequeue();
    }

    // Upper-middle of recent hits: crits count, a single lucky spike does not.
    static float TypicalHit()
    {
        if (Hits.Count < 8) return 0f;
        var s = Hits.OrderBy(x => x).ToList();
        return s[(int)(s.Count * 0.7f)];
    }

    internal static void Reset() => Hits.Clear();

    // Hits a fresh enemy of this kind should take at wave w.
    static float TargetHits(Kind k, int w)
    {
        float t = Mathf.Clamp01((w - AdaptFromWave) / 40f);
        float normal = Mathf.Lerp(3f, 6f, t) + Mathf.Max(0, w - 70) * 0.15f; // after 70: +1 hit per ~7 waves
        return k switch
        {
            Kind.Elite => normal * 3f,
            Kind.Boss => 40f + 1.2f * Mathf.Max(0, w - AdaptFromWave) + 2f * Mathf.Max(0, w - 70),
            Kind.Grandma => 200f + 2f * Mathf.Max(0, w - 60),
            _ => normal,
        };
    }

    internal static int AdaptHealth(int hp, Kind k, int w)
    {
        if (w < AdaptFromWave) return hp;
        float hit = TypicalHit();
        if (hit <= 0f) return hp;
        int need = Mathf.RoundToInt(hit * TargetHits(k, w));
        // Blend in over ten waves so the step from table to adaptive is never felt as a jump.
        float blend = Mathf.Clamp01((w - AdaptFromWave) / 10f);
        return Math.Max(hp, Mathf.RoundToInt(Mathf.Lerp(hp, need, blend)));
    }

    // Share of the player's max health one enemy hit should take at least.
    internal static float HitShare(Kind k, int w)
    {
        if (w < AdaptFromWave) return 0f;
        float t = Mathf.Clamp01((w - AdaptFromWave) / 40f);
        float blend = Mathf.Clamp01((w - AdaptFromWave) / 10f);
        float normal = (Mathf.Lerp(0.04f, 0.08f, t) + Mathf.Max(0, w - 70) * 0.002f) * blend;
        return k switch
        {
            Kind.Elite => normal * 1.6f,
            Kind.Boss => Mathf.Lerp(0.15f, 0.25f, t) * blend,
            Kind.Grandma => 0.30f * blend,
            _ => normal,
        };
    }

    // Which kind an attacker is (filled by the director when it spawns).
    internal static readonly Dictionary<IntPtr, Kind> KindOf = new();
}

// Player hits on horde enemies feed the meter.
[HarmonyPatch(typeof(EmeraldHealth), nameof(EmeraldHealth.DamageInternal))]
internal static class HitMeterPatch
{
    static void Prefix(int DamageAmount, Transform AttackerTransform, bool isTrapDamage)
    {
        if (!HordeGen.Active || isTrapDamage || AttackerTransform == null) return;
        var me = FirstPersonController.Instance;
        if (me == null) return;
        var root = AttackerTransform.root;
        if (root != null && root.Pointer == me.transform.root.Pointer) HordeBalance.RecordHit(DamageAmount);
    }
}

// Enemy hits on the player land at least as hard as the floor for that enemy kind.
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.TakeDamage), new[] { typeof(float), typeof(Transform) })]
internal static class HitFloorPatch
{
    static void Prefix(PlayerStats __instance, ref float amount, Transform attacker) => HitFloor.Apply(__instance, ref amount, attacker);
}

[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.TakeDamage), new[] { typeof(float), typeof(Transform), typeof(bool) })]
internal static class HitFloorBombPatch
{
    static void Prefix(PlayerStats __instance, ref float amount, Transform attacker) => HitFloor.Apply(__instance, ref amount, attacker);
}

internal static class HitFloor
{
    internal static void Apply(PlayerStats ps, ref float amount, Transform attacker)
    {
        if (!HordeGen.Active || attacker == null || amount <= 0f) return;
        int w = HordeDirector.State.Wave;
        var es = attacker.GetComponentInParent<EmeraldSystem>();
        if (es == null) return;
        var kind = HordeBalance.KindOf.TryGetValue(es.gameObject.Pointer, out var k) ? k : HordeBalance.Kind.Normal;
        float share = HordeBalance.HitShare(kind, w);
        if (share <= 0f) return;
        amount = Mathf.Max(amount, ps.MaxHealth * share);
    }
}
