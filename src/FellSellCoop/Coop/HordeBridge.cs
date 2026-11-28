using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FellSellHorde;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Coop;

// The only place that names the Horde mod's types (optional, like the Character mod: see
// CharacterBridge for the rules). It plugs this mod into the Horde mod's net seam (HordeCoop):
// the host runs the waves and sends snapshots, clients send the altar press and whether a gift
// choice is open on their screen. Enemies, bosses and their effects travel as on any floor
// (HostEnemies); gifts and the bank chest are each player's own.
internal static class HordeBridge
{
    public const byte MsgSnapshot = 15, MsgToHost = 16;

    static bool? _present;
    static bool _attached, _hooked;
    public static bool Present => _present ??= AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "FellSellHorde");

    // Client: the host's dungeon is a horde arena (from its state packets).
    internal static bool HostHorde;

    // This player's dungeon is a horde arena.
    public static bool Active => Present && ActiveInner();

    public static void Tick()
    {
        if (_attached || !Present) return;
        _attached = true;
        try { Attach(); } catch (Exception e) { CoopModule.Warn("horde link", e); }
    }

    // Before any session exists: the statue window's "Invite a friend" button opens the co-op window.
    // A Horde build older than that button has no such field; it keeps its placeholder.
    public static void Hook()
    {
        if (_hooked || !Present) return;
        _hooked = true;
        try { HookInner(); } catch (Exception e) { Plugin.L.LogInfo($"Co-op: this Horde mod has no invite button to drive ({e.GetType().Name})."); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void HookInner() => HordeCoop.Invite = CoopMenu.OpenInGame;

    // A client about to build the host's floor builds what the host has: the arena or a normal floor.
    public static void Arm(bool horde)
    {
        if (!Present) return;
        try { ArmInner(horde); } catch (Exception e) { CoopModule.Warn("horde arming", e); }
    }

    public static void FromHost(BinaryReader r) { if (Present) FromHostInner(Rest(r)); }
    public static void FromClient(BinaryReader r) { if (Present) FromClientInner(Rest(r)); }
    public static string Describe() => Present ? DescribeInner() + $" | hostHorde {HostHorde} shared {Shared()}" : "Horde mod not installed";
    public static string Press() { if (!Present) return "Horde mod not installed"; PressInner(); return "pressed"; }

    static byte[] Rest(BinaryReader r) => r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position));

    static bool Shared()
    {
        if (!CoopModule.Running || !HostEnemies.On || SceneManager.GetActiveScene().name != "DUNGEON") return false;
        if (!CoopSettings.IsHost) return HostEnemies.Client && HostHorde && DungeonSync.HostConnected && DungeonSync.BuildingHostFloor;
        // A partner who is down has no stand-in target but is still in the run.
        int seed = World.Key();
        if (seed == 0) return false;
        foreach (var rem in CoopModule.Remotes) if (rem.Scene == "DUNGEON" && rem.Seed == seed) return true;
        return false;
    }

    static byte[] Packet(byte type, byte[] data)
    {
        var w = CoopModule.Begin(type, out var ms);
        w.Write(data);
        return ms.ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool ActiveInner() => HordeCoop.Active;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ArmInner(bool horde)
    {
        if (HordeCoop.Armed == horde) return;
        HordeCoop.Armed = horde;
        Plugin.L.LogInfo(horde ? "Co-op: the host's dungeon is a horde arena, building it here too." : "Co-op: the host's dungeon is a normal floor, your own horde challenge is set aside.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Attach()
    {
        HordeCoop.Shared = Shared;
        HordeCoop.Host = () => CoopSettings.IsHost;
        HordeCoop.Partners = HostEnemies.ProxyBodies;
        HordeCoop.ToClients = data => CoopModule.SendAll(Packet(MsgSnapshot, data));
        HordeCoop.ToHost = data => CoopModule.SendToHost(Packet(MsgToHost, data));
        HordeCoop.Attach();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void FromHostInner(byte[] data) => HordeCoop.FromHost(data);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void FromClientInner(byte[] data) => HordeCoop.FromClient(data);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string DescribeInner() => HordeCoop.Describe();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void PressInner() => HordeCoop.PressPillar();

    public static string DevSeed(int seed) { if (!Present) return "Horde mod not installed"; SeedInner(seed); return "next horde floor seed " + seed; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SeedInner(int seed) => HordeCoop.DevSeed = seed;
}

// The Bows mod, reached by name only (it has two switches for this mod and nothing else): whether
// the arrow resolving its hit right now goes through shields, and a way for the host to say the
// same about a partner's arrow.
internal static class BowsBridge
{
    static bool _looked;
    static PropertyInfo _piercing;
    static FieldInfo _force, _pierced;

    static void Look()
    {
        if (_looked) return;
        _looked = true;
        try
        {
            var t = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FellSellBows")?.GetType("FellSellBows.BowsCoop");
            _piercing = t?.GetProperty("PiercingNow", BindingFlags.Public | BindingFlags.Static);
            _force = t?.GetField("ForcePierce", BindingFlags.Public | BindingFlags.Static);
            _pierced = t?.GetField("Pierced", BindingFlags.Public | BindingFlags.Static);
        }
        catch (Exception e) { CoopModule.Warn("bows link", e); }
    }

    public static bool PiercingNow { get { Look(); return _piercing != null && (bool)_piercing.GetValue(null); } }
    public static void Force(bool on) { Look(); _force?.SetValue(null, on); }
    public static bool Linked { get { Look(); return _force != null; } }
    public static int Pierced { get { Look(); return _pierced != null ? (int)_pierced.GetValue(null) : -1; } }
}
