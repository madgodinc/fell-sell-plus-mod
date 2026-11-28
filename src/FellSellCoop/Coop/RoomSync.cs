using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using Il2CppEmeraldAI;
using UnityEngine;

namespace FellSellPlus.Coop;

// Boss rooms and room events with host-run enemies (part of HostEnemies).
//
// Both players build the same floor, so its boss door, boss gate, boss spawner, arena altar, crypt
// tomb and cell lever stand at the same places in both games. Only the host's copies run for real
// (they spawn and watch the host's enemies). Twice a second the host reports the state of each, and a
// client moves its own copy into that state with the game's own methods: the doors close behind the
// boss fight, the gate opens when the boss is dead, the boss room pays out, a crypt opens and drops
// its loot. Nothing spawns on a client, its enemies are the host's.
//
// What a client starts (walking through the boss door, using an altar, a tomb or a lever) is asked of
// the host, which does it in its own game; the answer comes back in the next report.
internal static class RoomSync
{
    public const byte MsgState = 10, MsgAsk = 11;
    const float Interval = 0.5f, MatchRadius = 2f;

    internal enum Kind : byte { BossDoor = 1, BossGate, BossSpawner, Arena, Crypt, Lever }

    internal sealed class Obj
    {
        public Kind Kind; public Component C; public Vector3 Pos; public byte Bits, Extra;
    }

    struct Rec { public Kind Kind; public Vector3 Pos; public byte Bits, Extra; }

    static readonly List<Obj> _local = new();
    static readonly List<Rec> _host = new();
    static readonly HashSet<long> _logged = new();
    static float _nextSend, _nextApply, _hostAt = -100f, _nextAsk;
    static int _hostKey;

    static bool Shares => HostEnemies.Client && World.ClientShares;

    // Every boss and event object of this player's floor, with its state as two small numbers.
    internal static List<Obj> Scan()
    {
        _local.Clear();
        foreach (var t in UnityEngine.Object.FindObjectsOfType<BossDoorTrigger>())
            Add(Kind.BossDoor, t, t.HasTriggered ? 1 : 0, 0);
        foreach (var g in UnityEngine.Object.FindObjectsOfType<BossGate>())
            Add(Kind.BossGate, g, g.IsOpened ? 1 : 0, 0);
        foreach (var s in UnityEngine.Object.FindObjectsOfType<BossRoomSpawner>())
            Add(Kind.BossSpawner, s, (s._hasSpawned ? 1 : 0) | (s._bossDefeatProcessed ? 2 : 0), 0);
        foreach (var a in UnityEngine.Object.FindObjectsOfType<ArenaBattleEvent>())
            Add(Kind.Arena, a, (a.WaveInProgress ? 1 : 0) | (a.isInteractable ? 4 : 0)
                | (ChestOut(a.wave1Config) ? 8 : 0) | (ChestOut(a.wave2Config) ? 16 : 0) | (ChestOut(a.wave3Config) ? 32 : 0), a.CurrentWaveIndex);
        foreach (var c in UnityEngine.Object.FindObjectsOfType<CryptTombEvent>())
            Add(Kind.Crypt, c, (c.EventStarted ? 1 : 0) | (c.EventCompleted ? 2 : 0), 0);
        foreach (var l in UnityEngine.Object.FindObjectsOfType<CellLeverEvent>())
            Add(Kind.Lever, l, (l.EventStarted ? 1 : 0) | (l.EventCompleted ? 2 : 0), 0);
        return _local;
    }

    // An arena wave pays with a chest that waits switched off beside the altar.
    static bool ChestOut(ArenaBattleEvent.ArenaWaveConfig wave) =>
        wave != null && wave.waveRewardChest != null && wave.waveRewardChest.activeSelf;

    static void ShowChest(ArenaBattleEvent.ArenaWaveConfig wave, bool hostHasIt, Rec h, int n)
    {
        if (!hostHasIt || wave == null || wave.waveRewardChest == null || wave.waveRewardChest.activeSelf) return;
        Once(h, 30 + n, $"arena wave {n} is won on the host, its reward chest appears here");
        wave.waveRewardChest.SetActive(true);
    }

    static void Add(Kind kind, Component c, int bits, int extra) =>
        _local.Add(new Obj { Kind = kind, C = c, Pos = c.transform.position, Bits = (byte)bits, Extra = (byte)Mathf.Clamp(extra, 0, 255) });

    static Obj Nearest(Kind kind, Vector3 pos)
    {
        Obj best = null; float bestD = MatchRadius * MatchRadius;
        foreach (var o in _local)
        {
            if (o.Kind != kind) continue;
            float d = (o.Pos - pos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = o; }
        }
        return best;
    }

    // ---------------------------------------------------------------- host

    public static void HostPackets(List<byte[]> into)
    {
        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + Interval;
        int key = World.Key();
        if (key == 0 || key == World.ForestKey) return;
        var objs = Scan();
        if (objs.Count == 0) return;
        var w = CoopModule.Begin(MsgState, out var ms);
        int n = Mathf.Min(objs.Count, 70);
        w.Write(key); w.Write((byte)n);
        for (int i = 0; i < n; i++)
        {
            var o = objs[i];
            w.Write((byte)o.Kind); w.Write(o.Pos.x); w.Write(o.Pos.y); w.Write(o.Pos.z); w.Write(o.Bits); w.Write(o.Extra);
        }
        into.Add(ms.ToArray());
    }

    // A client walked through the boss door or used an altar, a tomb or a lever.
    public static void HostAsked(ulong peer, BinaryReader r)
    {
        var kind = (Kind)r.ReadByte();
        var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        if (!HostEnemies.On || World.Key() == 0) return;
        Scan();
        var o = Nearest(kind, pos);
        if (o == null) { Plugin.L.LogWarning($"Co-op rooms: a client used a {kind} at {pos}, but this floor has none there."); return; }
        string who = "a client";
        Vector3 at = pos;
        foreach (var kv in CoopModule.RemotePairs) if (kv.Key == peer) { who = kv.Value.Name; at = kv.Value.Pos; }
        try
        {
            switch (kind)
            {
                case Kind.BossDoor:
                    if ((o.Bits & 1) != 0) return;
                    Plugin.L.LogInfo($"Co-op rooms: {who} entered the boss room, the fight starts here too.");
                    EnterBossRoom(o.C.Cast<BossDoorTrigger>(), at);
                    break;
                case Kind.Arena:
                    Plugin.L.LogInfo($"Co-op rooms: {who} used the arena altar.");
                    o.C.Cast<ArenaBattleEvent>().Interact();
                    break;
                case Kind.Crypt:
                    if ((o.Bits & 1) != 0) return;
                    Plugin.L.LogInfo($"Co-op rooms: {who} opened the crypt tomb.");
                    o.C.Cast<CryptTombEvent>().Interact();
                    break;
                case Kind.Lever:
                    if ((o.Bits & 1) != 0) return;
                    Plugin.L.LogInfo($"Co-op rooms: {who} pulled the cell lever.");
                    o.C.Cast<CellLeverEvent>().Interact();
                    break;
            }
        }
        catch (Exception e) { CoopModule.Warn("room request " + kind, e); }
    }

    // The doors close behind whoever walked through them. The trigger sits in the doorway, so a player
    // whose own game has not fired it is still outside: he is brought in, a step past the partner who
    // started the fight, before his doors close too.
    static void EnterBossRoom(BossDoorTrigger door, Vector3 partnerFeet)
    {
        var fpc = FirstPersonController.Instance;
        if (fpc == null) return;
        var cc = fpc.characterController;
        Vector3 me = fpc.transform.position, inward = door.transform.position - me;
        inward.y = 0f;
        Vector3 spot = partnerFeet;
        if (inward.sqrMagnitude > 0.04f)
        {
            // A step further in than the partner, when there is floor and no wall on the way.
            Vector3 chest = partnerFeet + Vector3.up * 1.2f, step = inward.normalized * 1.5f;
            int solid = LayerMask.GetMask("Default", "Terrain", "Floor", "Wall");
            if (!Physics.Linecast(chest, chest + step, solid, QueryTriggerInteraction.Ignore)
                && Physics.Raycast(chest + step, Vector3.down, out var hit, 4f, solid, QueryTriggerInteraction.Ignore))
                spot = hit.point;
        }
        cc.enabled = false;
        fpc.transform.position = spot + Vector3.up * (cc.height * 0.5f - cc.center.y + 0.1f);
        cc.enabled = true;
        Plugin.L.LogInfo("Co-op rooms: brought into the boss room to your partner.");
        door.OnTriggerEnter(cc.Cast<Collider>());
    }

    // ---------------------------------------------------------------- client

    public static void Receive(BinaryReader r)
    {
        int key = r.ReadInt32();
        int n = r.ReadByte();
        _host.Clear();
        for (int i = 0; i < n; i++)
            _host.Add(new Rec { Kind = (Kind)r.ReadByte(), Pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), Bits = r.ReadByte(), Extra = r.ReadByte() });
        _hostKey = key; _hostAt = Time.unscaledTime;
    }

    public static void Tick()
    {
        if (Time.unscaledTime < _nextApply) return;
        _nextApply = Time.unscaledTime + Interval;
        if (!Shares || _hostKey != World.ClientKey || Time.unscaledTime - _hostAt > 3f || Gather.Holding) return;
        Scan();
        foreach (var h in _host)
        {
            var o = Nearest(h.Kind, h.Pos);
            if (o == null) { Once(h, 0, $"the host's {h.Kind} at {h.Pos} has no match on this floor"); continue; }
            try { Apply(h, o); }
            catch (Exception e) { CoopModule.Warn("room state " + h.Kind, e); }
        }
    }

    static void Once(Rec h, int what, string text)
    {
        long id = ((long)h.Kind << 56) ^ ((long)what << 48) ^ ((long)Mathf.RoundToInt(h.Pos.x * 2f) << 32) ^ ((long)Mathf.RoundToInt(h.Pos.z * 2f) << 8) ^ (long)(World.ClientKey & 0xFF);
        if (_logged.Count > 500) _logged.Clear();
        if (_logged.Add(id)) Plugin.L.LogInfo("Co-op rooms: " + text + ".");
    }

    static void Apply(Rec h, Obj o)
    {
        switch (h.Kind)
        {
            case Kind.BossDoor:
            {
                bool hostIn = (h.Bits & 1) != 0, meIn = (o.Bits & 1) != 0;
                if (hostIn && !meIn)
                {
                    Once(h, 1, "the boss fight started on the host, the doors close here too");
                    Vector3 at = o.Pos;
                    foreach (var rem in CoopModule.Remotes) if (rem.IsHost) at = rem.Pos;
                    EnterBossRoom(o.C.Cast<BossDoorTrigger>(), at);
                }
                else if (meIn && !hostIn) Ask(h.Kind, o.Pos); // this client walked in first
                break;
            }
            case Kind.BossGate:
                if ((h.Bits & 1) != 0 && (o.Bits & 1) == 0)
                {
                    Once(h, 1, "the host's boss gate is open, opening it here");
                    o.C.Cast<BossGate>().ForceOpen();
                }
                break;
            case Kind.BossSpawner:
                if ((h.Bits & 2) != 0 && (o.Bits & 2) == 0)
                {
                    Once(h, 1, "the boss is defeated on the host, the boss room pays out here");
                    var sp = o.C.Cast<BossRoomSpawner>();
                    sp._hasSpawned = true;
                    // The game's own end of a boss fight, with nobody left to be alive.
                    sp.CheckAllEnemiesDead(new Il2CppSystem.Collections.Generic.List<EmeraldHealth>());
                }
                break;
            case Kind.Crypt:
            {
                var crypt = o.C.Cast<CryptTombEvent>();
                if ((h.Bits & 2) != 0 && (o.Bits & 2) == 0)
                {
                    Once(h, 2, "the crypt is cleared on the host, it opens here");
                    crypt._eventStarted = true;
                    crypt.StartCoroutine(crypt.CompleteCryptEventRoutine());
                }
                else if ((h.Bits & 1) != 0 && (o.Bits & 3) == 0)
                {
                    Once(h, 1, "the crypt fight started on the host");
                    crypt.SetFightingStateImmediately();
                    // Its own watch over the fight would see no enemies here and end it at once.
                    crypt.StopAllCoroutines();
                    crypt._eventStarted = true; crypt._eventCompleted = false;
                }
                break;
            }
            case Kind.Lever:
            {
                var lever = o.C.Cast<CellLeverEvent>();
                if ((h.Bits & 2) != 0 && (o.Bits & 2) == 0)
                {
                    Once(h, 2, "the cell fight is over on the host");
                    lever.SetCompletedStateImmediately();
                    lever._eventStarted = true; lever._eventCompleted = true;
                }
                else if ((h.Bits & 1) != 0 && (o.Bits & 3) == 0)
                {
                    Once(h, 1, "the cell lever was pulled on the host");
                    lever.SetFightingStateImmediately();
                    lever.StopAllCoroutines(); // as with the crypt
                    lever._eventStarted = true; lever._eventCompleted = false;
                }
                break;
            }
            case Kind.Arena:
            {
                var arena = o.C.Cast<ArenaBattleEvent>();
                bool hostWave = (h.Bits & 1) != 0, myWave = (o.Bits & 1) != 0;
                if (hostWave != myWave)
                {
                    Once(h, hostWave ? 10 + h.Extra : 20 + h.Extra, hostWave ? $"arena wave {h.Extra + 1} started on the host" : "the arena wave is over on the host");
                    arena._waveInProgress = hostWave;
                    arena.StartCoroutine(arena.MoveGateRoutine(hostWave));
                    if (hostWave) arena.PlayWaveAudio();
                }
                if (arena._currentWaveIndex != h.Extra) arena._currentWaveIndex = h.Extra;
                bool hostUsable = (h.Bits & 4) != 0;
                if (arena.isInteractable != hostUsable) arena.isInteractable = hostUsable;
                ShowChest(arena.wave1Config, (h.Bits & 8) != 0, h, 1);
                ShowChest(arena.wave2Config, (h.Bits & 16) != 0, h, 2);
                ShowChest(arena.wave3Config, (h.Bits & 32) != 0, h, 3);
                break;
            }
        }
    }

    static void Ask(Kind kind, Vector3 pos)
    {
        if (Time.unscaledTime < _nextAsk) return;
        _nextAsk = Time.unscaledTime + 0.5f;
        var w = CoopModule.Begin(MsgAsk, out var ms);
        w.Write((byte)kind); w.Write(pos.x); w.Write(pos.y); w.Write(pos.z);
        CoopModule.SendToHost(ms.ToArray());
        Plugin.L.LogInfo($"Co-op rooms: asking the host to start the {kind} at {pos}.");
    }

    // A client using an altar, a tomb or a lever: its own copy would spawn nothing (the enemies are the
    // host's), so the host's copy is used instead.
    static bool ClientUses(Kind kind, Component c)
    {
        if (!Shares) return true;
        try { _nextAsk = 0f; Ask(kind, c.transform.position); }
        catch (Exception e) { CoopModule.Warn("room use", e); }
        return false;
    }

    [HarmonyPatch(typeof(ArenaBattleEvent), nameof(ArenaBattleEvent.Interact))]
    internal static class ArenaUsePatch { static bool Prefix(ArenaBattleEvent __instance) => ClientUses(Kind.Arena, __instance); }

    [HarmonyPatch(typeof(CryptTombEvent), nameof(CryptTombEvent.Interact))]
    internal static class CryptUsePatch { static bool Prefix(CryptTombEvent __instance) => ClientUses(Kind.Crypt, __instance); }

    [HarmonyPatch(typeof(CellLeverEvent), nameof(CellLeverEvent.Interact))]
    internal static class LeverUsePatch { static bool Prefix(CellLeverEvent __instance) => ClientUses(Kind.Lever, __instance); }

    // ---------------------------------------------------------------- dev

    internal static string Describe()
    {
        var sb = new StringBuilder();
        foreach (var o in Scan()) sb.AppendLine($"{o.Kind} {o.C.name} at {o.Pos} bits {o.Bits} extra {o.Extra}");
        if (!CoopSettings.IsHost) sb.AppendLine($"host report: {_host.Count} objects, {Time.unscaledTime - _hostAt:0.0} s old, key {_hostKey}");
        return sb.ToString().TrimEnd();
    }
}
