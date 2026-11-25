using System.Collections.Generic;
using Il2CppEmeraldAI;
using UnityEngine;

namespace FellSellPlus.Skills;

internal static class SkillUtil
{
    static PlayerCombat _combat;
    static PlayerSkillEffectsController _effects;

    public static PlayerCombat Combat =>
        _combat != null ? _combat : (_combat = Object.FindObjectOfType<PlayerCombat>());

    public static PlayerSkillEffectsController Effects =>
        _effects != null ? _effects : (_effects = Object.FindObjectOfType<PlayerSkillEffectsController>());

    public static Transform Player => PlayerStats.Instance != null ? PlayerStats.Instance.transform : null;

    public static EmeraldHealth HealthOf(GameObject go) =>
        go == null ? null : go.GetComponentInParent<EmeraldHealth>();

    public static bool Alive(EmeraldHealth h) => h != null && h.CurrentHealth > 0 && !h.Immortal;

    // Living enemies whose colliders touch the sphere, nearest first.
    public static List<EmeraldHealth> EnemiesAround(Vector3 center, float radius)
    {
        var seen = new HashSet<System.IntPtr>();
        var found = new List<EmeraldHealth>();
        foreach (var col in Physics.OverlapSphere(center, radius))
        {
            var h = col != null ? col.GetComponentInParent<EmeraldHealth>() : null;
            if (!Alive(h) || !seen.Add(h.Pointer)) continue;
            found.Add(h);
        }
        found.Sort((a, b) => (a.transform.position - center).sqrMagnitude.CompareTo((b.transform.position - center).sqrMagnitude));
        return found;
    }
}
