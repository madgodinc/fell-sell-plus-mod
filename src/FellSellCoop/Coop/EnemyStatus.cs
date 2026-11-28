using System;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace FellSellPlus.Coop;

// Statuses of the host's enemies on a client (part of HostEnemies).
//
// Elites: the host says which of its enemies are elite and with which affixes; the client makes its
// copy the same elite through the game's own InitializeElite. The copy then shows the aura, the badge
// and the light, and its frost aura slows this client when he walks into it, exactly as in a solo game
// (EliteEnemyController.UpdateFrostAuraLogic runs on the copy against the local player).
//
// Freeze and curse (the mage's and the occultist's skills): the host reports for every enemy whether it
// is frozen or cursed, and a client shows that on its copy with the game's own effect scripts. When a
// client's own skill freezes or curses a copy, the host is told and does it to the real enemy, which
// stops moving for both players.
internal static class EnemyStatus
{
    public const byte Msg = 14;
    const byte Frozen = 1, Cursed = 2;
    const byte KindFreeze = 1, KindCurse = 2;

    static PlayerSkillEffectsController _skills;
    static bool _applying;
    internal static int ElitesMade, FreezesSent, CursesSent, FreezesApplied, CursesApplied, FreezesShown, CursesShown;

    static PlayerSkillEffectsController Skills()
    {
        if (_skills == null) _skills = UnityEngine.Object.FindObjectOfType<PlayerSkillEffectsController>();
        return _skills;
    }

    // ---------------------------------------------------------------- host

    public static byte HostFlags(GameObject enemy)
    {
        byte f = 0;
        if (enemy.GetComponent<MageFreezeEffect>() != null) f |= Frozen;
        if (enemy.GetComponent<OccultCurseEffect>() != null) f |= Cursed;
        return f;
    }

    // 0 = not an elite; else affix + 1 in the low three bits, the second affix + 1 in the next three, 64 = elite plus.
    public static byte EliteByte(EliteEnemyController elite)
    {
        if (elite == null || !elite._isInitialized) return 0;
        int b = (int)elite._affix + 1;
        if (elite._hasSecondaryAffix) b |= ((int)elite._secondaryAffix + 1) << 3;
        if (elite._isElitePlus) b |= 64;
        return (byte)b;
    }

    // A client's skill froze or cursed its copy of one of this host's enemies.
    public static void HostReceive(BinaryReader r)
    {
        int id = r.ReadInt32(); byte kind = r.ReadByte(); float duration = r.ReadSingle(), bonus = r.ReadSingle();
        var go = HostEnemies.TrackedObject(id);
        var skills = Skills();
        if (!HostEnemies.On || go == null || skills == null) return;
        duration = Mathf.Clamp(duration, 0.2f, 30f);
        _applying = true;
        try
        {
            if (kind == KindFreeze)
            {
                skills.TriggerFreezeEnemy(go, duration);
                if (FreezesApplied++ < 6) Plugin.L.LogInfo($"Co-op statuses: a client froze {go.name} for {duration:0.0} s.");
            }
            else if (kind == KindCurse)
            {
                var curse = go.GetComponent<OccultCurseEffect>();
                if (curse == null) { curse = go.AddComponent<OccultCurseEffect>(); curse.Initialize(duration, bonus, skills._purpleCurseMaterial); }
                else curse.RefreshDuration(duration, bonus);
                if (CursesApplied++ < 6) Plugin.L.LogInfo($"Co-op statuses: a client cursed {go.name} for {duration:0.0} s (+{bonus:0}% damage).");
            }
        }
        catch (Exception e) { CoopModule.Warn("status from a client", e); }
        finally { _applying = false; }
    }

    // ---------------------------------------------------------------- client

    public static void MakeElite(GameObject copy, byte elite)
    {
        if (elite == 0 || copy == null) return;
        try
        {
            if (copy.GetComponent<EliteEnemyController>() != null) return;
            var affix = (EliteAffix)((elite & 7) - 1);
            int second = (elite >> 3) & 7;
            var c = copy.AddComponent<EliteEnemyController>();
            c.InitializeElite(affix, second > 0 ? new Il2CppSystem.Nullable<EliteAffix>((EliteAffix)(second - 1)) : new Il2CppSystem.Nullable<EliteAffix>(), (elite & 64) != 0);
            if (ElitesMade++ < 8) Plugin.L.LogInfo($"Co-op statuses: the host's {copy.name} is an elite here too ({affix}{(second > 0 ? " + " + (EliteAffix)(second - 1) : "")}).");
        }
        catch (Exception e) { CoopModule.Warn("elite copy", e); }
    }

    // The copy dies through the game's own death; an explosive elite would then throw a second bomb
    // next to the host's real one, and that one would hurt for real.
    public static void BeforeDeath(GameObject copy)
    {
        try
        {
            var c = copy.GetComponent<EliteEnemyController>();
            if (c != null && c.HasAffix(EliteAffix.Explosive)) UnityEngine.Object.DestroyImmediate(c);
        }
        catch (Exception e) { CoopModule.Warn("elite copy death", e); }
    }

    // Shows the host's freeze and curse on a copy while the host reports them, and ends them when it stops.
    public static void Apply(GameObject copy, byte status, ref bool frozenSeen, ref bool cursedSeen)
    {
        var skills = Skills();
        if (skills == null || copy == null) return;
        _applying = true;
        try
        {
            var freeze = copy.GetComponent<MageFreezeEffect>();
            if ((status & Frozen) != 0)
            {
                if (freeze == null) { skills.TriggerFreezeEnemy(copy, 1.2f); if (FreezesShown++ < 6) Plugin.L.LogInfo($"Co-op statuses: {copy.name} is frozen on the host, shown here."); }
                else if (freeze._timer < 1.2f) freeze._timer = 1.2f;
                frozenSeen = true;
            }
            else if (frozenSeen)
            {
                frozenSeen = false;
                if (freeze != null) UnityEngine.Object.Destroy(freeze);
            }
            var curse = copy.GetComponent<OccultCurseEffect>();
            if ((status & Cursed) != 0)
            {
                if (curse == null) { curse = copy.AddComponent<OccultCurseEffect>(); curse.Initialize(1.2f, 0f, skills._purpleCurseMaterial); if (CursesShown++ < 6) Plugin.L.LogInfo($"Co-op statuses: {copy.name} is cursed on the host, shown here."); }
                else if (curse._timer < 1.2f) curse._timer = 1.2f;
                cursedSeen = true;
            }
            else if (cursedSeen)
            {
                cursedSeen = false;
                if (curse != null) UnityEngine.Object.Destroy(curse);
            }
        }
        catch (Exception e) { CoopModule.Warn("enemy status", e); }
        finally { _applying = false; }
    }

    static void Tell(GameObject enemyGo, byte kind, float duration, float bonus)
    {
        if (_applying || !HostEnemies.Client) return;
        int id = HostEnemies.CopyId(enemyGo);
        if (id == 0) return;
        var w = CoopModule.Begin(Msg, out var ms);
        w.Write(id); w.Write(kind); w.Write(duration); w.Write(bonus);
        CoopModule.SendToHost(ms.ToArray());
    }

    [HarmonyPatch(typeof(PlayerSkillEffectsController), nameof(PlayerSkillEffectsController.TriggerFreezeEnemy))]
    internal static class FreezePatch
    {
        static void Postfix(GameObject enemyGo, float duration)
        {
            try { Tell(enemyGo, KindFreeze, duration, 0f); if (!_applying && HostEnemies.Client) FreezesSent++; }
            catch (Exception e) { CoopModule.Warn("freeze to the host", e); }
        }
    }

    [HarmonyPatch(typeof(PlayerSkillEffectsController), nameof(PlayerSkillEffectsController.ApplyCurseToEnemy))]
    internal static class CursePatch
    {
        static void Postfix(GameObject enemyGo)
        {
            try
            {
                if (_applying || !HostEnemies.Client || enemyGo == null) return;
                // What the skill just put on the copy says how long and how strong this player's curse is.
                var health = enemyGo.GetComponentInParent<Il2CppEmeraldAI.EmeraldHealth>();
                var curse = health != null ? health.GetComponent<OccultCurseEffect>() : null;
                if (curse == null) return;
                Tell(enemyGo, KindCurse, curse.RemainingDuration, curse.DamageBonusPercent);
                CursesSent++;
            }
            catch (Exception e) { CoopModule.Warn("curse to the host", e); }
        }
    }

    internal static string Describe() =>
        $"elites made {ElitesMade} | freezes sent {FreezesSent} applied {FreezesApplied} shown {FreezesShown} | curses sent {CursesSent} applied {CursesApplied} shown {CursesShown}";
}
