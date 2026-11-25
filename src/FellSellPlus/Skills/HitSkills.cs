using System;
using HarmonyLib;
using Il2CppEmeraldAI;
using UnityEngine;
using static FellSellPlus.Skills.SkillRegistry;

namespace FellSellPlus.Skills;

// On-hit skills (Execution, Whirlwind, Counterstrike, Bloodletter, Rot) hook the enemy side of a hit:
// EmeraldHealth.DamageInternal(int amount, Transform attacker, int ragdoll, bool crit, bool trap)
// (0x1806F1000, called by Damage and DamageFromTrap). They used to hook
// PlayerSkillEffectsController.OnAttackLanded, but that method's by-ref bool/float parameters broke
// the IL2CPP trampoline (NullReferenceException on every swing). Plain value parameters
// here are safe; the amount is changed through the prefix argument.
[HarmonyPatch(typeof(EmeraldHealth), "DamageInternal")]
internal static class PlayerHitSkillsPatch
{
    static readonly float[] ExecBonus = { 0f, 0.30f, 0.50f, 0.75f };
    static readonly float[] Counter = { 0f, 0.40f, 0.60f, 0.80f };
    static readonly float[] RotPct = { 0f, 0.02f, 0.03f, 0.04f };
    static bool _inside; // our own extra hits (Whirlwind, Rot) must not trigger skills again

    static void Prefix(EmeraldHealth __instance, ref int __0, Transform __1, bool __3, bool __4)
    {
        if (_inside || __instance == null || __4 || __0 <= 0) return;
        var player = SkillUtil.Player;
        if (player == null || __1 == null || !(__1 == player || __1.IsChildOf(player))) return;
        try
        {
            float dmg = __0;

            int exec = Rank(FighterExecution);
            if (exec > 0 && __instance.StartingHealth > 0 && __instance.CurrentHealth < __instance.StartingHealth * 0.25f)
                dmg *= 1f + ExecBonus[Math.Min(exec, 3)];

            int c = Rank(KnightCounterstrike);
            if (c > 0 && Time.time - SkillState.LastBlock <= 2f)
            {
                dmg *= 1f + Counter[Math.Min(c, 3)];
                SkillState.LastBlock = -99f;
            }
            __0 = Mathf.Max(1, Mathf.RoundToInt(dmg));

            int b = Rank(OccultBloodletter);
            if (b > 0) PlayerStats.Instance?.Heal(Math.Min(b, 3));

            int rot = Rank(OccultRot);
            if (rot > 0) RotTicker.Apply(__instance, RotPct[Math.Min(rot, 3)]);

            if (Rank(FighterWhirlwind) > 0 && Time.time - WhirlwindDashPatch.LastDash <= 2f)
            {
                WhirlwindDashPatch.LastDash = -99f;
                _inside = true;
                try
                {
                    foreach (var h in SkillUtil.EnemiesAround(player.position, 3.5f))
                        if (h.Pointer != __instance.Pointer) h.Damage(__0, player, 100, __3);
                    var pm = PrayersManager.Instance;
                    var fx = UnityEngine.Object.FindObjectOfType<PlayerSkillEffectsController>();
                    if (pm != null && pm.whirlwindVfxPrefab != null && fx != null)
                        fx.SpawnVFX(pm.whirlwindVfxPrefab, player.position, Quaternion.identity, 1.5f, null, 1f);
                }
                finally { _inside = false; }
            }
        }
        catch (Exception e) { Plugin.L.LogWarning($"On-hit skills: {e.Message}"); }
    }
}
