using System;
using System.IO;
using HarmonyLib;
using Il2CppEmeraldAI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Coop;

// Death in a shared dungeon. A player who dies while a partner is alive on the same floor does not go
// through the game's death: he is down (cannot move, cannot be hurt, keeps everything) and waits. When
// a living partner stands by the exit portal circle, the one who is down gets up next to it with half
// his health. During a boss fight the circle is behind the locked gate, so there the partner stands
// next to the one who is down for a few seconds instead, and he gets up where he lies. When nobody is left to do that (the partner died too, left or disconnected), the game's
// own death runs as usual.
//
// Leaving together: when the host is back in the village and a client is still in the dungeon, the
// client is taken home through the game's own exit portal.
internal static class Revive
{
    public const byte Msg = 7;
    const float Grace = 2f;       // a partner missing for this long cannot save anyone
    const float MinDown = 3f;     // a partner already standing at the portal does not make death a blink
    const float BesideRadius = 2.5f;

    internal static bool Down { get; private set; }
    static bool _bypass;
    static float _alone = -1f, _nextCall, _nextExit, _downAt;
    static bool _hostWasInDungeon;
    static float _beside, _nextLockCheck;
    static bool _locked;

    // Seconds left until the partner this player stands next to gets up (-1 = not reviving anybody).
    internal static float Reviving { get; private set; } = -1f;

    // A boss fight is on: the doors closed behind the players and the gate to the portal is still shut.
    internal static bool BossLocked
    {
        get
        {
            if (Time.unscaledTime < _nextLockCheck) return _locked;
            _nextLockCheck = Time.unscaledTime + 0.5f;
            _locked = false;
            try
            {
                // A horde arena keeps its portal room shut for ten waves at a time, and the forest has no
                // portal circle at all: the same rule as a boss fight.
                if (HordeBridge.Active || SceneManager.GetActiveScene().name == "FOREST") return _locked = true;
                bool fight = false, shut = false;
                foreach (var d in UnityEngine.Object.FindObjectsOfType<BossDoorTrigger>()) if (d.HasTriggered) { fight = true; break; }
                if (fight) foreach (var g in UnityEngine.Object.FindObjectsOfType<BossGate>()) if (!g.IsOpened) { shut = true; break; }
                _locked = fight && shut;
            }
            catch (Exception e) { CoopModule.Warn("boss room state", e); }
            return _locked;
        }
    }

    static int MyFloor => DungeonManager.Instance != null ? DungeonManager.Instance.currentFloor : -1;

    // Where players can go down and be revived: a dungeon floor, or the forest.
    static bool RunScene(string scene) => scene == "DUNGEON" || scene == "FOREST";
    // This partner is in the same place as this player (the same dungeon floor, or the forest).
    static bool Here(CoopModule.Remote rem, string scene, int floor) => rem.Scene == scene && (scene != "DUNGEON" || rem.Floor == floor);

    // Somebody alive on this floor who could walk to the portal.
    static bool PartnerCanSave()
    {
        int floor = MyFloor; string scene = SceneManager.GetActiveScene().name;
        if (!RunScene(scene)) return false;
        foreach (var rem in CoopModule.Remotes)
            if (Here(rem, scene, floor) && !rem.Down && Time.unscaledTime - rem.LastSeen < Grace) return true;
        return false;
    }

    internal static CoopModule.Remote DownPartner()
    {
        int floor = MyFloor; string scene = SceneManager.GetActiveScene().name;
        if (!RunScene(scene)) return null;
        foreach (var rem in CoopModule.Remotes)
            if (Here(rem, scene, floor) && rem.Down) return rem;
        return null;
    }

    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.Die))]
    internal static class DiePatch
    {
        static bool Prefix(PlayerStats __instance)
        {
            if (_bypass || !CoopSettings.On || !CoopModule.Running) return true;
            if (Down) return false;
            if (!PartnerCanSave()) return true;
            GoDown(__instance);
            return false;
        }
    }

    static void GoDown(PlayerStats stats)
    {
        Down = true; _alone = -1f; _downAt = Time.unscaledTime;
        Plugin.L.LogInfo(BossLocked ? "Co-op: you are down. A partner standing next to you brings you back." : "Co-op: you are down. A partner at the exit portal circle brings you back.");
        Hold(stats, true);
    }

    static void Hold(PlayerStats stats, bool down)
    {
        try
        {
            var fpc = FirstPersonController.Instance;
            if (stats != null)
            {
                stats.IsInvulnerable = down;
                if (down && stats._currentHealth < 1f) stats._currentHealth = 1f;
            }
            if (fpc != null)
            {
                fpc.canMove = !down;
                // The host's own enemies leave a player who is down alone.
                var ignored = EmeraldDetection.IgnoredTargetsList;
                if (ignored != null)
                {
                    if (down && !ignored.Contains(fpc.transform)) ignored.Add(fpc.transform);
                    else if (!down) ignored.Remove(fpc.transform);
                }
            }
        }
        catch (Exception e) { CoopModule.Warn("down state", e); }
    }

    public static void Tick()
    {
        string scene = SceneManager.GetActiveScene().name;
        var fpc = FirstPersonController.Instance;
        var stats = PlayerStats.Instance;
        if (Down)
        {
            if (!RunScene(scene) || fpc == null || stats == null) { Down = false; Hold(stats, false); return; }
            if (fpc.canMove) fpc.canMove = false;
            if (PartnerCanSave()) { _alone = -1f; return; }
            if (_alone < 0f) { _alone = Time.unscaledTime; return; }
            if (Time.unscaledTime - _alone < Grace) return;
            // Nobody can come any more: the normal death.
            Plugin.L.LogInfo("Co-op: no partner is left alive on this floor, the game's own death follows.");
            Down = false; Hold(stats, false);
            _bypass = true;
            try { stats._currentHealth = 0f; stats.Die(); } finally { _bypass = false; }
            return;
        }
        if (scene != "DUNGEON") _hostWasInDungeon = false;
        if (!RunScene(scene) || fpc == null) return;
        if (scene == "DUNGEON") FollowHostHome();

        var fallen = DownPartner();
        if (fallen == null) { _beside = 0f; Reviving = -1f; return; }
        if (BossLocked)
        {
            // The circle is out of reach: standing next to him for a while does it.
            Vector3 gap = fallen.Pos - fpc.transform.position;
            float up = Mathf.Abs(gap.y); gap.y = 0f;
            if (gap.magnitude > BesideRadius || up > 3f || stats == null || stats.IsDead) { _beside = 0f; Reviving = -1f; return; }
            _beside += Time.unscaledDeltaTime;
            float need = Mathf.Max(1f, CoopSettings.ReviveSeconds.Value);
            Reviving = Mathf.Max(0f, need - _beside);
            if (_beside < need || Time.unscaledTime < _nextCall) return;
            _nextCall = Time.unscaledTime + 0.5f;
            var bw = CoopModule.Begin(Msg, out var bms);
            bw.Write(fallen.Pos.x); bw.Write(fallen.Pos.y); bw.Write(fallen.Pos.z); bw.Write(fallen.Yaw + 180f);
            CoopModule.SendAll(bms.ToArray());
            return;
        }
        _beside = 0f; Reviving = -1f;
        // Standing by the portal circle with a partner down: call him over.
        if (Time.unscaledTime < _nextCall) return;
        var portal = DungeonTeleporter.Instance;
        if (portal == null) return;
        Vector3 feet = fpc.transform.position, d = portal.transform.position - feet;
        float dy = Mathf.Abs(d.y); d.y = 0f;
        if (d.magnitude > CoopSettings.ReviveRadius.Value || dy > 4f) return;
        _nextCall = Time.unscaledTime + 0.5f;
        // He gets up beside the caller, who stands by the circle on ground known to be free.
        Vector3 spot = fpc.transform.position, side = fpc.transform.right * 0.9f;
        if (!Physics.Linecast(spot, spot + side, LayerMask.GetMask("Default", "Terrain", "Floor", "Wall"), QueryTriggerInteraction.Ignore)) spot += side;
        var w = CoopModule.Begin(Msg, out var ms);
        w.Write(spot.x); w.Write(spot.y); w.Write(spot.z); w.Write(fpc.transform.eulerAngles.y);
        CoopModule.SendAll(ms.ToArray());
    }

    public static void Receive(BinaryReader r)
    {
        var spot = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        float yaw = r.ReadSingle();
        if (!Down || Time.unscaledTime - _downAt < MinDown) return;
        GetUp(spot, yaw);
    }

    internal static string GetUp(Vector3 spot, float yaw)
    {
        var fpc = FirstPersonController.Instance;
        var stats = PlayerStats.Instance;
        if (fpc == null || stats == null) return "no player";
        Down = false;
        Hold(stats, false);
        var cc = fpc.CharacterController;
        // The floor under the spot, when there is one: the circle's own height is not the walking surface.
        if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out var hit, 6f, LayerMask.GetMask("Default", "Terrain", "Floor"), QueryTriggerInteraction.Ignore)) spot = hit.point;
        cc.enabled = false;
        fpc.transform.SetPositionAndRotation(spot + Vector3.up * (cc.height * 0.5f - cc.center.y + 0.1f), Quaternion.Euler(0f, yaw + 180f, 0f));
        cc.enabled = true;
        try { stats.Heal(Mathf.Max(0f, stats.MaxHealth * 0.5f - stats.CurrentHealth)); } catch (Exception e) { CoopModule.Warn("revive heal", e); }
        Plugin.L.LogInfo($"Co-op: revived by a partner with {stats.CurrentHealth:0}/{stats.MaxHealth:0} health.");
        return "up";
    }

    // The host went home through the portal: a client that follows the host goes too.
    static void FollowHostHome()
    {
        if (CoopSettings.IsHost || !CoopSettings.FollowHost.Value || !DungeonSync.HostConnected) return;
        var stats = PlayerStats.Instance;
        if (stats == null || stats.IsDead) return; // dying is its own way home
        if (DungeonSync.HostScene == "DUNGEON") { _hostWasInDungeon = true; return; }
        if (!_hostWasInDungeon || DungeonSync.HostScene != "MAIN" || Time.unscaledTime < _nextExit) return;
        _nextExit = Time.unscaledTime + 20f;
        var portal = DungeonTeleporter.Instance;
        if (portal == null) { Plugin.L.LogWarning("Co-op: the host left the dungeon, but this floor has no exit portal to follow through."); return; }
        Plugin.L.LogInfo("Co-op: the host left the dungeon, leaving through the portal too.");
        try { portal.ExecuteTeleport("MAIN", true); }
        catch (Exception e) { CoopModule.Warn("leaving with the host", e); }
    }
}
