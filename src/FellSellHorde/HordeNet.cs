using System;
using System.Collections.Generic;
using UnityEngine;

namespace FellSellHorde;

// Seam for the future co-op mod. The director never asks "where is THE player" or acts on input
// directly: it asks the net layer for every player, and only the authority (the host) runs waves,
// spawns enemies and changes phases. Clients send commands and apply snapshots.
// Solo play uses LocalHordeNet. With FellSellCoop installed, HordeCoop.Attach puts CoopHordeNet in
// its place (host: broadcasts snapshots, receives client commands; client: the reverse).

internal enum HordePhase { Off, Ready, Wave, Intermission, Break }

internal enum HordeCommand { Continue }

// Everything a client needs to mirror the run (UI, side-room lock). Enemies are synced separately,
// by the co-op enemy layer.
internal struct HordeSnapshot
{
    public HordePhase Phase;
    public int Wave;          // 1-based wave number, 0 before the first wave
    public int Alive;
    public int LeftToSpawn;
    public int Seed;
    // Counted by the authority since the run began: every player's own bank chest gets one item
    // (his own roll) per boss.
    public int BossKills, GrandmaKills;
    public int Bosses;        // bosses walking with the current wave

    internal byte[] ToBytes()
    {
        var ms = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(ms);
        w.Write((byte)Phase); w.Write(Wave); w.Write(Alive); w.Write(LeftToSpawn); w.Write(Seed);
        w.Write(BossKills); w.Write(GrandmaKills); w.Write((byte)Bosses);
        return ms.ToArray();
    }

    internal static HordeSnapshot FromBytes(byte[] data)
    {
        var r = new System.IO.BinaryReader(new System.IO.MemoryStream(data));
        return new HordeSnapshot
        {
            Phase = (HordePhase)r.ReadByte(), Wave = r.ReadInt32(), Alive = r.ReadInt32(), LeftToSpawn = r.ReadInt32(), Seed = r.ReadInt32(),
            BossKills = r.ReadInt32(), GrandmaKills = r.ReadInt32(), Bosses = r.ReadByte(),
        };
    }
}

internal interface IHordeNet
{
    bool IsAuthority { get; }
    int PlayerCount { get; }
    // Every player's body in this scene, the local one included.
    IEnumerable<Transform> Players();
    // Authority -> everyone.
    void Publish(HordeSnapshot s);
    // Anyone -> authority (the authority's own commands are handled locally).
    void Send(HordeCommand c);
    // Raised on the authority for commands from any player, itself included.
    event Action<HordeCommand> Command;
    // Raised on clients when a snapshot arrives.
    event Action<HordeSnapshot> Snapshot;
    // A player says whether a gift choice is open on his screen; the authority holds the next wave
    // back while any partner's is.
    void ReportBusy(bool busy);
    bool PartnerBusy { get; }
}

internal static class HordeNet
{
    public static IHordeNet Current = new LocalHordeNet();
}

internal sealed class LocalHordeNet : IHordeNet
{
    public bool IsAuthority => true;
    public int PlayerCount => 1;

    public IEnumerable<Transform> Players()
    {
        var p = FirstPersonController.Instance;
        if (p != null) yield return p.transform;
    }

    public void Publish(HordeSnapshot s) { }
    public void Send(HordeCommand c) => Command?.Invoke(c);
    public void ReportBusy(bool busy) { }
    public bool PartnerBusy => false;
    public event Action<HordeCommand> Command;
#pragma warning disable CS0067 // raised only by the co-op implementation
    public event Action<HordeSnapshot> Snapshot;
#pragma warning restore CS0067
}

// What the co-op mod plugs into: the only public surface of this mod. FellSellCoop fills in the
// delegates once (Attach) and passes the two kinds of packets through; this mod never names it.
public static class HordeCoop
{
    // A partner plays this horde run with us right now.
    public static Func<bool> Shared;
    // We are the one who runs the waves.
    public static Func<bool> Host;
    // Host: the bodies of the other players in the arena (what the host's enemies can target).
    public static Func<List<Transform>> Partners;
    public static Action<byte[]> ToClients, ToHost;
    // Set as soon as the co-op mod is loaded (no session needed): opens its host and invite window
    // over the village. The argument runs when that window closes. False = it could not be shown.
    public static Func<Action, bool> Invite;

    public static bool Active => HordeGen.Active;
    // A client entering the host's dungeon takes the host's choice: horde arena or normal floor.
    public static bool Armed { get => HordeGen.Armed; set => HordeGen.Armed = value; }

    public static void Attach()
    {
        if (HordeNet.Current is CoopHordeNet) return;
        HordeNet.Current = new CoopHordeNet();
        Plugin.L.Msg("Horde: co-op link attached.");
    }

    // Client: a snapshot from the host.
    public static void FromHost(byte[] data)
    {
        try { (HordeNet.Current as CoopHordeNet)?.OnSnapshot(HordeSnapshot.FromBytes(data)); }
        catch (Exception e) { Plugin.L.Warning("Horde: bad snapshot: " + e.Message); }
    }

    // Host: a command (0, command) or a busy flag (1, 0|1) from a client.
    public static void FromClient(byte[] data)
    {
        if (data == null || data.Length < 2 || HordeNet.Current is not CoopHordeNet net) return;
        if (data[0] == 0) net.OnCommand((HordeCommand)data[1]);
        else if (data[0] == 1) net.OnPartnerBusy(data[1] != 0);
    }

    // For the co-op mod's test commands.
    public static int DevSeed;
    public static string Describe() => HordeDirector.Describe();
    public static void PressPillar() => HordeDirector.PillarPressed();
}

// Solo it behaves exactly like LocalHordeNet; with a partner in the arena the host is the authority.
internal sealed class CoopHordeNet : IHordeNet
{
    const float BusyFor = 1.5f;   // a busy flag holds this long after the last report
    const float BusyCap = 90f;    // a partner who never picks does not stop the run for good
    float _busyUntil = -1f, _busySince = -1f;

    static bool Shared => HordeCoop.Shared != null && HordeCoop.Shared();
    public bool IsAuthority => !Shared || HordeCoop.Host();
    public int PlayerCount => !Shared ? 1 : IsAuthority ? 1 + (HordeCoop.Partners?.Invoke().Count ?? 0) : 2;

    public IEnumerable<Transform> Players()
    {
        var p = FirstPersonController.Instance;
        if (p != null) yield return p.transform;
        if (!Shared || !IsAuthority || HordeCoop.Partners == null) yield break;
        foreach (var t in HordeCoop.Partners()) if (t != null) yield return t;
    }

    public void Publish(HordeSnapshot s) { if (Shared && IsAuthority) HordeCoop.ToClients?.Invoke(s.ToBytes()); }

    public void Send(HordeCommand c)
    {
        if (IsAuthority) Command?.Invoke(c);
        else HordeCoop.ToHost?.Invoke(new[] { (byte)0, (byte)c });
    }

    public void ReportBusy(bool busy) { if (!IsAuthority) HordeCoop.ToHost?.Invoke(new[] { (byte)1, (byte)(busy ? 1 : 0) }); }

    public bool PartnerBusy => Shared && Time.unscaledTime < _busyUntil && Time.unscaledTime - _busySince < BusyCap;

    internal void OnPartnerBusy(bool busy)
    {
        if (!busy) { _busyUntil = -1f; _busySince = -1f; return; }
        if (Time.unscaledTime > _busyUntil) _busySince = Time.unscaledTime;
        _busyUntil = Time.unscaledTime + BusyFor;
    }

    internal void OnCommand(HordeCommand c) { if (IsAuthority) Command?.Invoke(c); }
    internal void OnSnapshot(HordeSnapshot s) { if (!IsAuthority) Snapshot?.Invoke(s); }

    public event Action<HordeCommand> Command;
    public event Action<HordeSnapshot> Snapshot;
}
