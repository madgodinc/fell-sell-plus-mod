using System;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Coop;

// Developer test commands for co-op, behind the main mod's Dev/Commands switch (off by default).
// Lines in UserData/FellSellPlus/coop_cmd.txt run once; results are appended to coop_out.txt.
internal static class CoopDev
{
    static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus");
    static float _next, _walkUntil;
    static Vector3 _walkVelocity;

    public static void Tick()
    {
        if (!Dev.DevCommands.Enabled.Value) return;
        Walk();
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.3f;
        var roleArg = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("--fsp-role="));
        string suffix = roleArg != null ? "_" + roleArg.Substring(11) : "";
        var file = Path.Combine(Dir, $"coop_cmd{suffix}.txt");
        if (!File.Exists(file)) return;
        string[] lines;
        try { lines = File.ReadAllLines(file); File.Delete(file); } catch { return; }
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            string result;
            try { result = Run(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)); }
            catch (Exception e) { result = "ERROR " + e; }
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] > {line}\n{result}");
        }
        File.AppendAllText(Path.Combine(Dir, $"coop_out{suffix}.txt"), sb.ToString());
    }

    // dev: the curse message a client's skill would send for this copy.
    static byte[] DevCurse(GameObject go)
    {
        var w = CoopModule.Begin(EnemyStatus.Msg, out var ms);
        w.Write(HostEnemies.CopyId(go)); w.Write((byte)2); w.Write(6f); w.Write(25f);
        return ms.ToArray();
    }

    // Moves the hero through his own character controller, so his velocity (and with it the walking
    // animation the partner sees) is real.
    static void Walk()
    {
        if (Time.unscaledTime >= _walkUntil) { CoopModule.FakeMove = Vector2.zero; return; }
        var fpc = FirstPersonController.Instance;
        if (fpc != null) fpc.CharacterController.Move(_walkVelocity * Time.deltaTime);
    }

    // A place to stand about dist metres from a point: open line to it and floor underneath.
    internal static bool SafeSpot(Vector3 center, Vector3 prefer, float dist, out Vector3 spot)
    {
        prefer.y = 0; if (prefer.sqrMagnitude < 0.01f) prefer = Vector3.forward;
        prefer.Normalize();
        var chest = center + Vector3.up * 1.2f;
        for (int i = 0; i < 12; i++)
        {
            var dir = Quaternion.Euler(0, i * 30f * (i % 2 == 0 ? 0.5f : -0.5f) * 2f, 0) * prefer;
            var at = chest + dir * dist;
            if (Physics.Linecast(chest, at, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (!Physics.Raycast(at, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
            spot = hit.point;
            return true;
        }
        spot = center;
        return false;
    }

    internal static void Put(FirstPersonController fpc, Vector3 feet, Vector3 lookAt)
    {
        var cc = fpc.CharacterController;
        cc.enabled = false; fpc.transform.position = feet + Vector3.up * (cc.height * 0.5f - cc.center.y + 0.1f); cc.enabled = true;
        var d = lookAt - feet; d.y = 0;
        if (d.sqrMagnitude > 0.01f) fpc.transform.rotation = Quaternion.LookRotation(d);
        fpc.verticalRotation = 0f;
    }

    static string Run(string[] a)
    {
        var fpc = FirstPersonController.Instance;
        switch (a[0])
        {
            case "status":
            {
                var sb = new StringBuilder();
                DungeonSync.Current(out int seed, out int floor);
                var dm = DungeonManager.Instance; var ps = PlayerStats.Instance;
                sb.AppendLine($"me: scene {SceneManager.GetActiveScene().name} floor {floor} seed {seed} pos {(fpc != null ? fpc.transform.position.ToString() : "-")} running {CoopModule.Running} timeScale {Time.timeScale} focused {Application.isFocused} lobby {SteamLobby.InLobby} waiting {DungeonSync.Waiting} hostReady {DungeonSync.HostReady} onHostFloor {DungeonSync.OnHostFloor} down {Revive.Down} hp {(ps != null ? ps.CurrentHealth : -1):0}/{(ps != null ? ps.MaxHealth : -1):0} record {(dm != null ? dm.maxFloorReached : -1)} enemies tracked {HostEnemies.TrackedCount} copies {HostEnemies.GhostCount} standins {HostEnemies.ProxyCount} frame {Time.frameCount}");
                foreach (var r in CoopModule.Remotes)
                    sb.AppendLine($"{r.Name}: scene {r.Scene} floor {r.Floor} seed {r.Seed} pos {r.Pos} yaw {r.Yaw:0} move ({r.Body.MoveX:0.0}, {r.Body.MoveY:0.0}) act {r.Body.Action}#{r.Body.ActionSeq} packets {r.Packets} down {r.Down} held {(r.Held?.Length ?? 0)} body {(r.Puppet != null ? "puppet" : r.Avatar != null ? (r.Fallback ? "capsule" : "villager") : "none")} look {(r.Look?.Length ?? 0)} bytes drawnAt {(r.Root != null ? r.Root.position.ToString() : "-")}");
                return sb.ToString().TrimEnd();
            }
            case "walk": // walk <forward m/s> <right m/s> <seconds>: relative to where the hero faces
            {
                var t = fpc.transform;
                _walkVelocity = t.forward * float.Parse(a[1]) + t.right * float.Parse(a[2]);
                _walkUntil = Time.unscaledTime + float.Parse(a[3]);
                CoopModule.FakeMove = new Vector2(float.Parse(a[2]), float.Parse(a[1]));
                return "walking";
            }
            case "yaw":
                fpc.transform.rotation = Quaternion.Euler(0f, float.Parse(a[1]), 0f);
                return "ok";
            case "facepartner": // turn the hero (and the camera) toward the first partner in this scene
            {
                foreach (var r in CoopModule.Remotes)
                {
                    var d = r.Pos - fpc.transform.position; d.y = 0;
                    if (d.sqrMagnitude < 0.01f) continue;
                    fpc.transform.rotation = Quaternion.LookRotation(d);
                    fpc.verticalRotation = 0f;
                    return $"facing {r.Name}, {d.magnitude:0.0} m away";
                }
                return "no partner";
            }
            case "nearpartner": // nearpartner <metres> [behind]: stand that far from the first partner, facing him
            {
                float dist = a.Length > 1 ? float.Parse(a[1]) : 3f;
                foreach (var r in CoopModule.Remotes)
                {
                    var dir = Quaternion.Euler(0, r.Yaw, 0) * Vector3.forward;
                    if (a.Length > 2) dir = -dir;
                    bool ok = SafeSpot(r.Pos, dir, dist, out var spot);
                    if (!ok) spot = r.Pos + dir * dist;
                    Put(fpc, spot, r.Pos);
                    return $"standing {dist} m from {r.Name}{(ok ? "" : " (no clear spot found)")}";
                }
                return "no partner";
            }
            case "nearenemy": // nearenemy <metres>: stand by the nearest living enemy, facing it
            {
                float dist = a.Length > 1 ? float.Parse(a[1]) : 3f;
                Il2CppEmeraldAI.EmeraldSystem best = null; float bestD = float.MaxValue;
                foreach (var es in UnityEngine.Object.FindObjectsOfType<Il2CppEmeraldAI.EmeraldSystem>())
                {
                    var h = es.HealthComponent;
                    if (h != null && h.CurrentHealth <= 0) continue;
                    float d = (es.transform.position - fpc.transform.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = es; }
                }
                if (best == null) return "no enemy";
                var p = best.transform.position;
                if (!SafeSpot(p, best.transform.forward, dist, out var spot)) return $"no clear spot by {best.name} at {p}";
                Put(fpc, spot, p);
                return $"by {best.name} at {p}";
            }
            case "act": // act <PuppetAction number>: the partner sees this one-shot
                CoopModule.FakeAction = byte.Parse(a[1]); CoopModule.FakeSeq++;
                return "sent";
            case "floor": // floor <n>: set this player's floor number (takes effect on the next load)
                DungeonManager.Instance.currentFloor = int.Parse(a[1]);
                return "floor " + DungeonManager.Instance.currentFloor;
            case "nextfloor": // what the game's own floor step does
                DungeonManager.Instance.NextFloor();
                return $"floor {DungeonManager.Instance.currentFloor} scene {SceneManager.GetActiveScene().name}";
            case "loaddungeon": // reload the dungeon scene through the game's loading screen
            {
                var menu = UnityEngine.Object.FindObjectOfType<MainPauseMenu>();
                if (menu == null) return "no MainPauseMenu";
                menu.LoadSceneWithLoadingScreen(a.Length > 1 ? a[1] : "DUNGEON", true);
                return "loading";
            }
            case "unpause": // close the pause menu (the game opens it when its window loses focus)
            {
                var menu = UnityEngine.Object.FindObjectOfType<MainPauseMenu>();
                if (menu == null) return "no MainPauseMenu";
                if (!menu.IsPauseMenuOpen) return "not paused";
                menu.HandleCancelAction();
                return $"closed, pause menu open {menu.IsPauseMenuOpen}";
            }
            case "stairs": // where the way down is; "stairs go" steps into it like a player would
            {
                var sb = new StringBuilder();
                foreach (var t in UnityEngine.Object.FindObjectsOfType<NextFloorTrigger>())
                {
                    sb.AppendLine($"{t.name} {t.transform.position}");
                    if (a.Length > 1 && a[1] == "go")
                    {
                        var cc = fpc.CharacterController;
                        cc.enabled = false; fpc.transform.position = t.transform.position + Vector3.up * 0.5f; cc.enabled = true;
                        return "stepped into " + t.name;
                    }
                }
                return sb.Length == 0 ? "no NextFloorTrigger" : sb.ToString().TrimEnd();
            }
            case "probe": // what the player and the nearest enemy are made of (targets, layers, colliders)
            {
                var sb = new StringBuilder();
                string Comps(GameObject g) => string.Join(",", g.GetComponents<Component>().Select(c => c == null ? "null" : c.GetIl2CppType().Name));
                void Cols(Transform root, int max)
                {
                    int n = 0;
                    foreach (var c in root.GetComponentsInChildren<Collider>(true))
                    {
                        if (n++ >= max) { sb.AppendLine("  ..."); break; }
                        sb.AppendLine($"  col {c.name} [{c.GetIl2CppType().Name}] layer {LayerMask.LayerToName(c.gameObject.layer)} tag {c.tag} trigger {c.isTrigger} enabled {c.enabled} size {c.bounds.size}");
                    }
                }
                var pg = fpc.gameObject;
                sb.AppendLine($"player {pg.name} tag {pg.tag} layer {pg.layer}={LayerMask.LayerToName(pg.layer)} comps {Comps(pg)}");
                Cols(pg.transform, 10);
                var br = UnityEngine.Object.FindObjectOfType<Il2CppEmeraldAI.EmeraldGeneralTargetBridgeCustom>();
                if (br != null) sb.AppendLine($"bridge on {br.gameObject.name} tag {br.tag} layer {br.gameObject.layer} health {br.Health}/{br.StartHealth} immortal {br.Immortal} col {(br.m_Collider != null ? br.m_Collider.name : "-")}");
                var pc = fpc._playerCombat;
                if (pc != null) sb.AppendLine($"hitLayer {pc.hitLayer.value}");
                var layers = new StringBuilder();
                for (int i = 0; i < 32; i++) { var ln = LayerMask.LayerToName(i); if (!string.IsNullOrEmpty(ln)) layers.Append($"{i}={ln} "); }
                sb.AppendLine("layers " + layers);
                Il2CppEmeraldAI.EmeraldSystem best = null; float bestD = float.MaxValue;
                foreach (var es in UnityEngine.Object.FindObjectsOfType<Il2CppEmeraldAI.EmeraldSystem>())
                {
                    float d = (es.transform.position - fpc.transform.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = es; }
                }
                if (best != null)
                {
                    var eg = best.gameObject; var det = best.DetectionComponent; var h = best.HealthComponent;
                    sb.AppendLine($"enemy {eg.name} at {eg.transform.position} dist {Mathf.Sqrt(bestD):0.0} tag {eg.tag} layer {eg.layer}={LayerMask.LayerToName(eg.layer)} hp {(h != null ? h.CurrentHealth : -1)}/{(h != null ? h.StartingHealth : -1)}");
                    sb.AppendLine("  comps " + Comps(eg));
                    if (det != null) sb.AppendLine($"  detection mask {det.DetectionLayerMask.value} playerTag {det.PlayerTag} radius {det.DetectionRadius} state {det.CurrentDetectionState} pick {det.PickTargetType} los {det.RequireLineOfSight} faction {det.CurrentFaction}");
                    var ti = best.CurrentTargetInfo;
                    sb.AppendLine($"  target {(ti != null && ti.TargetSource != null ? ti.TargetSource.name : "-")} lbd {(best.LBDComponent != null)} agent {(best.m_NavMeshAgent != null ? best.m_NavMeshAgent.enabled.ToString() : "-")}");
                    var rb = eg.GetComponent<RoomBoundEnemy>();
                    if (rb != null) sb.AppendLine($"  roomBound aggroed {rb.HasAggroed} room {rb.RoomID} inside {rb.IsInsideRoomBounds(fpc.transform.position)}");
                    var an = best.AIAnimator;
                    if (an != null) sb.AppendLine($"  animator root {an.applyRootMotion} layers {an.layerCount} params {string.Join(",", an.parameters.Select(x => x.name))}");
                    Cols(eg.transform, 14);
                }
                var tp = DungeonTeleporter.Instance;
                if (tp != null) sb.AppendLine($"teleporter {tp.name} at {tp.transform.position}");
                return sb.ToString().TrimEnd();
            }
            case "enemies": // every enemy object here: health, target, whether its AI runs
            {
                var sb = new StringBuilder();
                foreach (var es in UnityEngine.Object.FindObjectsOfType<Il2CppEmeraldAI.EmeraldSystem>())
                {
                    var h = es.HealthComponent; var ti = es.CurrentTargetInfo;
                    float d = fpc != null ? (es.transform.position - fpc.transform.position).magnitude : -1f;
                    sb.AppendLine($"{es.gameObject.name} hp {(h != null ? h.CurrentHealth : -1)}/{(h != null ? h.StartingHealth : -1)} at {es.transform.position} dist {d:0.0} ai {es.enabled} target {(ti != null && ti.TargetSource != null ? ti.TargetSource.name : "-")}");
                }
                return sb.Length == 0 ? "no enemies" : sb.ToString().TrimEnd();
            }
            case "hit": // hit <damage>: strike the nearest living enemy the way a weapon does
            {
                Il2CppEmeraldAI.EmeraldHealth best = null; float bestD = float.MaxValue;
                foreach (var h in UnityEngine.Object.FindObjectsOfType<Il2CppEmeraldAI.EmeraldHealth>())
                {
                    if (h.CurrentHealth <= 0) continue;
                    float d = (h.transform.position - fpc.transform.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = h; }
                }
                if (best == null) return "no enemy";
                int before = best.CurrentHealth;
                // "hit 20 none": a blow from nobody, to tell blocking (which needs an attacker in front) from immunity.
                best.Damage(int.Parse(a[1]), a.Length > 2 ? null : fpc.transform, 100, false);
                return $"hit {best.gameObject.name}: {before} -> {best.CurrentHealth} (immortal {best.Immortal})";
            }
            case "god": // god on|off: enemy blows do nothing (to watch a fight without dying)
                PlayerStats.Instance.IsInvulnerable = a.Length < 2 || a[1] != "off";
                return "invulnerable " + PlayerStats.Instance.IsInvulnerable;
            case "die": // take a lethal blow
                PlayerStats.Instance.TakeDamage(100000f, null);
                return $"hp {PlayerStats.Instance.CurrentHealth} dead {PlayerStats.Instance.IsDead} down {Revive.Down}";
            case "portal": // portal [go <metres>]: where the exit circle is; "go" stands that far from it
            {
                var tp = DungeonTeleporter.Instance;
                if (tp == null) return "no DungeonTeleporter";
                var p = tp.transform.position;
                if (a.Length > 1 && a[1] == "go")
                {
                    float dist = a.Length > 2 ? float.Parse(a[2]) : 1.5f;
                    if (!SafeSpot(p, fpc.transform.position - p, dist, out var spot)) return $"no clear spot by the portal at {p}";
                    Put(fpc, spot, p);
                }
                var d = p - fpc.transform.position; d.y = 0;
                return $"portal at {p}, {d.magnitude:0.0} m away";
            }
            case "getup": // dev: what a partner at the portal does for a player who is down
                return Revive.GetUp(fpc.transform.position, fpc.transform.eulerAngles.y);
            case "exit": // leave the dungeon through the game's own portal
            {
                var tp = DungeonTeleporter.Instance;
                if (tp == null) return "no DungeonTeleporter";
                if (a.Length > 1 && a[1] == "interact") tp.Interact(); else tp.ExecuteTeleport("MAIN", true);
                return "teleporting";
            }
            case "follow":
                return DungeonSync.GoToHostFloor();
            case "rooms": // which biome has which custom rooms (arena, crypt, cells) and on which floor
            {
                var gen = CustomDungeonGenerator.Instance;
                if (gen == null) return "no generator (enter the dungeon first)";
                var sb = new StringBuilder();
                var maps = gen.biomeFloorMappings;
                for (int i = 0; maps != null && i < maps.Count; i++)
                {
                    var m = maps[i]; var b = m.biome;
                    sb.Append($"floors {m.minFloor}-{m.maxFloor} {(b != null ? b.biomeName : "?")}:");
                    var rooms = b != null ? b.customRooms : null;
                    for (int r = 0; rooms != null && r < rooms.Count; r++)
                        sb.Append($" [{rooms[r].roomName} prefab {(rooms[r].roomPrefab != null ? rooms[r].roomPrefab.name : "-")} floor {rooms[r].targetFloorLevel} {rooms[r].placementType}]");
                    sb.AppendLine();
                }
                return sb.Length == 0 ? "no biome mappings" : sb.ToString().TrimEnd();
            }
            case "slow": // slow <seconds>: follow the host's next floor change this late
                DungeonSync.DevDelayFollow(float.Parse(a[1]));
                return "will follow late";
            case "bosschest": // bosschest [open]: the nearest boss chest and what it holds
            {
                BossChest best = null; float bestD = float.MaxValue;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<BossChest>())
                {
                    float d = (c.transform.position - fpc.transform.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (best == null) return "no boss chest";
                var sb = new StringBuilder($"{best.name} at {best.transform.position} ({Mathf.Sqrt(bestD):0.0} m) opened {best.isOpened} usable {best.isInteractable}:");
                var inv = best.inventory;
                for (int i = 0; inv != null && i < inv.Count; i++) sb.Append($" {(inv[i].item != null ? inv[i].item.name : "-")} x{inv[i].amount}");
                if (a.Length > 1 && a[1] == "open")
                {
                    if (SafeSpot(best.transform.position, fpc.transform.position - best.transform.position, 2f, out var spot)) Put(fpc, spot, best.transform.position);
                    best.Interact(); sb.Append(" opened now");
                }
                return sb.ToString();
            }
            case "trap": // trap [go]: the nearest trap hurtbox; "go" stands in it
            {
                TrapHurtbox best = null; float bestD = float.MaxValue;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<TrapHurtbox>())
                {
                    float d = (c.transform.position - fpc.transform.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (best == null) return "no trap";
                if (a.Length > 1 && a[1] == "hit") // what the trap does when its blade touches the player
                {
                    float before = PlayerStats.Instance.CurrentHealth;
                    best.ProcessImpact(fpc.characterController.Cast<Collider>());
                    return $"{best.name} struck: hp {before:0} -> {PlayerStats.Instance.CurrentHealth:0}";
                }
                if (a.Length > 1 && a[1] == "go")
                {
                    // On the floor under it (a swinging blade hangs from the ceiling), a step along the corridor.
                    var at = best.transform.position;
                    if (a.Length > 2) at += best.transform.right * float.Parse(a[2]);
                    foreach (var hit in Physics.RaycastAll(at + Vector3.up * 0.5f, Vector3.down, 12f, LayerMask.GetMask("Default", "Terrain", "Floor"), QueryTriggerInteraction.Ignore))
                        if (hit.point.y < at.y) at = hit.point; // the lowest surface is the walking floor
                    Put(fpc, at, at + fpc.transform.forward);
                }
                return $"{best.name} (parent {(best.transform.parent != null ? best.transform.parent.name : "-")}) at {best.transform.position} damage {best.damage}, {Mathf.Sqrt(bestD):0.0} m away, hp {PlayerStats.Instance.CurrentHealth:0}";
            }
            case "world": // boss and event objects of this floor with their state, traps, chests, effects, loot cut
            {
                var sb = new StringBuilder();
                var gen = CustomDungeonGenerator.Instance; var dm = DungeonManager.Instance; var wm = WindowsManager.Instance;
                sb.AppendLine($"key {World.Key()} clientShares {World.ClientShares} floor {(dm != null ? dm.currentFloor : -1)} bossFloor {(gen != null ? gen.IsBossFloor.ToString() : "-")} bossEvery {(gen != null ? gen.bossRoomInterval : -1)} bossRoom '{DungeonSync.BossRoom}' gatherOpen {Gather.Open} hostOpen {Gather.HostOpen} holding {Gather.Holding} canMove {(fpc != null ? fpc.canMove.ToString() : "-")}");
                sb.AppendLine($"paused {(wm != null ? wm.IsGamePaused.ToString() : "-")} forceUnpaused {(wm != null ? wm.forceUnpaused.ToString() : "-")} timeScale {Time.timeScale} effects live {HostEffects.LiveCount} copies {HostEffects.CopyCount} loot: {CoopLoot.Stats}");
                sb.AppendLine($"traps {UnityEngine.Object.FindObjectsOfType<PressurePlateTrap>().Length} hurtboxes {UnityEngine.Object.FindObjectsOfType<TrapHurtbox>().Length} chests {UnityEngine.Object.FindObjectsOfType<DungeonChest>().Length} bossChests {UnityEngine.Object.FindObjectsOfType<BossChest>().Length}");
                sb.Append(RoomSync.Describe());
                return sb.ToString().TrimEnd();
            }
            case "goto": // goto <Kind> [n] [metres]: stand by the n-th boss or event object of that kind, facing it
            case "use":  // use <Kind> [n]: what a player does to it (walk through the boss door, use the altar, tomb or lever)
            {
                var kind = (RoomSync.Kind)Enum.Parse(typeof(RoomSync.Kind), a[1], true);
                int nth = a.Length > 2 ? int.Parse(a[2]) : 0, i = 0;
                foreach (var o in RoomSync.Scan())
                {
                    if (o.Kind != kind || i++ != nth) continue;
                    if (a[0] == "goto")
                    {
                        float dist = a.Length > 3 ? float.Parse(a[3]) : 2.5f;
                        bool ok = SafeSpot(o.Pos, fpc.transform.position - o.Pos, dist, out var spot);
                        Put(fpc, ok ? spot : o.Pos, o.Pos);
                        return $"at {o.C.name} {o.Pos}{(ok ? "" : " (no clear spot, standing on it)")}";
                    }
                    switch (kind)
                    {
                        case RoomSync.Kind.BossDoor: o.C.Cast<BossDoorTrigger>().OnTriggerEnter(fpc.characterController.Cast<Collider>()); break;
                        case RoomSync.Kind.Arena: o.C.Cast<ArenaBattleEvent>().Interact(); break;
                        case RoomSync.Kind.Crypt: o.C.Cast<CryptTombEvent>().Interact(); break;
                        case RoomSync.Kind.Lever: o.C.Cast<CellLeverEvent>().Interact(); break;
                        case RoomSync.Kind.BossGate: o.C.Cast<BossGate>().ForceOpen(); break;
                        default: return "nothing to use";
                    }
                    return $"used {o.C.name} at {o.Pos}";
                }
                return "no such object";
            }
            case "pool": // the host's live pooled effects and projectiles
            {
                var sb = new StringBuilder();
                foreach (var m in UnityEngine.Object.FindObjectsOfType<Il2CppEmeraldAI.Utility.EmeraldObjectPool.PoolMember>())
                    sb.AppendLine($"{m.gameObject.name} at {m.transform.position}");
                return sb.Length == 0 ? "nothing pooled is active" : sb.ToString().TrimEnd();
            }
            case "prefabs": // prefabs [regex]: enemy prefabs loaded in this scene's assets
            {
                var rx = new System.Text.RegularExpressions.Regex(a.Length > 1 ? a[1] : ".", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var names = new System.Collections.Generic.SortedSet<string>();
                foreach (var es in Resources.FindObjectsOfTypeAll<Il2CppEmeraldAI.EmeraldSystem>())
                    if (!es.gameObject.scene.IsValid() && rx.IsMatch(es.gameObject.name)) names.Add(es.gameObject.name);
                return names.Count == 0 ? "none" : string.Join(", ", names);
            }
            case "spawn": // spawn <regex> [metres]: put one enemy of the first matching prefab in front of the player
            {
                var rx = new System.Text.RegularExpressions.Regex(a[1], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                float dist = a.Length > 2 ? float.Parse(a[2]) : 7f;
                foreach (var es in Resources.FindObjectsOfTypeAll<Il2CppEmeraldAI.EmeraldSystem>())
                {
                    var prefab = es.gameObject;
                    if (prefab.scene.IsValid() || !rx.IsMatch(prefab.name)) continue;
                    var feet = fpc.transform.position;
                    if (!SafeSpot(feet, fpc.transform.forward, dist, out var spot)) return "no clear spot";
                    if (UnityEngine.AI.NavMesh.SamplePosition(spot, out var nav, 3f, UnityEngine.AI.NavMesh.AllAreas)) spot = nav.position;
                    var d = feet - spot; d.y = 0;
                    var go = UnityEngine.Object.Instantiate(prefab, spot, Quaternion.LookRotation(d));
                    go.name = prefab.name;
                    return $"spawned {prefab.name} at {spot}";
                }
                return "no such prefab";
            }
            case "menu": // open or close the pause menu the way the player's key does
            {
                var wm = WindowsManager.Instance;
                if (wm == null) return "no WindowsManager";
                wm.HandlePauseInput();
                return $"paused {wm.IsGamePaused} forceUnpaused {wm.forceUnpaused} timeScale {Time.timeScale} windows {wm.HasActiveWindows}";
            }
            case "chest": // chest [open]: the nearest dungeon chest and what it holds
            {
                DungeonChest best = null; float bestD = float.MaxValue;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<DungeonChest>())
                {
                    float d = (c.transform.position - fpc.transform.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (best == null) return "no chest";
                var sb = new StringBuilder($"{best.name} at {best.transform.position} ({Mathf.Sqrt(bestD):0.0} m):");
                var inv = best.inventory;
                for (int i = 0; inv != null && i < inv.Count; i++) sb.Append($" {(inv[i].item != null ? inv[i].item.name : "-")} x{inv[i].amount}");
                if (a.Length > 1 && a[1] == "open") { best.Interact(); sb.Append(" opened"); }
                return sb.ToString();
            }
            case "chests": // total of what every chest on the floor holds (to compare the loot cut)
            {
                int n = 0, stacks = 0, items = 0;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<DungeonChest>())
                {
                    n++;
                    var inv = c.inventory;
                    for (int i = 0; inv != null && i < inv.Count; i++) if (inv[i].item != null) { stacks++; items += inv[i].amount; }
                }
                return $"{n} chests, {stacks} stacks, {items} items; {CoopLoot.Stats}";
            }
            case "horde": // horde [press|arm|disarm]: the horde run's state here; press = the altar's Start/Continue
                if (a.Length > 1 && a[1] == "press") return HordeBridge.Press();
                if (a.Length > 2 && a[1] == "seed") return HordeBridge.DevSeed(int.Parse(a[2]));
                if (a.Length > 1 && (a[1] == "arm" || a[1] == "disarm")) { HordeBridge.Arm(a[1] == "arm"); return a[1] + "ed"; }
                return HordeBridge.Describe();
            case "stunproxy": return HostEnemies.DevStunProxy(a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 2f); // host: an enemy's stun on the first client's stand-in
            case "stunme": // what an enemy's stun does to this player in his own game
            {
                var own = fpc.GetComponent<Il2CppEmeraldAI.EmeraldGeneralTargetBridgeCustom>();
                if (own == null) return "no player bridge";
                own.TriggerStun(a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 2f);
                return $"stunned: canMove {fpc.canMove} speed {fpc.CurrentSpeed:0.00}";
            }
            case "doors": case "traps": case "forest": case "prayers": return WorldSync.Dev(a); // phase 6: shared doors, traps, forest event, prayer rules
            case "sounds": return EnemySounds.Describe(); // enemy voices sent (host) and replayed (client)
            case "statuses": return EnemyStatus.Describe();
            case "coopmenu": return CoopMenu.Dev(a); // coopmenu [open|close|host|solo|join n|invite n|continue]
            case "difficulty": return DungeonSync.DifficultyLine(Progression.GameQuery.IsRussian()) ?? "no host difficulty known (host, or not connected)";
            case "bosslock": return $"boss room locked {Revive.BossLocked} reviving {Revive.Reviving:0.0} down {Revive.Down}";
            case "speed": return $"speed {fpc.CurrentSpeed:0.00} velocity {fpc.characterController.velocity.magnitude:0.00}";
            case "tele": // the village's ways into a run
            {
                var sb = new StringBuilder();
                foreach (var t in UnityEngine.Object.FindObjectsOfType<ForestTeleporter>()) sb.Append($"{t.name} -> {t.sceneName} spawn '{t.targetSpawnPointName}' at {t.transform.position} | ");
                return sb.Length == 0 ? "no ForestTeleporter here" : sb.ToString();
            }
            case "enterrun": return DungeonSync.EnterRun(a.Length > 1 ? a[1] : "DUNGEON");
            case "freeze": // freeze [seconds] / curse: what the mage's and the occultist's skills do to the nearest enemy
            case "curse":
            case "elite": // elite <Explosive|Regeneration|Armored|FrostAura|Vampiric>: make the nearest enemy an elite (host)
            {
                Il2CppEmeraldAI.EmeraldSystem best = null; float bestD = float.MaxValue;
                foreach (var es in UnityEngine.Object.FindObjectsOfType<Il2CppEmeraldAI.EmeraldSystem>())
                {
                    var h = es.HealthComponent;
                    if (h == null || h.CurrentHealth <= 0) continue;
                    float d = (es.transform.position - fpc.transform.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = es; }
                }
                if (best == null) return "no living enemy";
                var skills = UnityEngine.Object.FindObjectOfType<PlayerSkillEffectsController>();
                var go = best.gameObject;
                if (a[0] == "freeze") skills.TriggerFreezeEnemy(go, a.Length > 1 ? float.Parse(a[1]) : 4f);
                else if (a[0] == "curse")
                {
                    skills.ApplyCurseToEnemy(go);
                    // Without the occultist's skill the game's call does nothing: put the effect on directly.
                    if (go.GetComponent<OccultCurseEffect>() == null) { go.AddComponent<OccultCurseEffect>().Initialize(6f, 25f, skills._purpleCurseMaterial); CoopModule.SendToHost(DevCurse(go)); }
                }
                else
                {
                    var affix = (EliteAffix)Enum.Parse(typeof(EliteAffix), a[1], true);
                    var el = go.GetComponent<EliteEnemyController>() ?? go.AddComponent<EliteEnemyController>();
                    el.InitializeElite(affix, new Il2CppSystem.Nullable<EliteAffix>(), false);
                }
                return $"{a[0]} on {go.name} at {Mathf.Sqrt(bestD):0.0} m: frozen {go.GetComponent<MageFreezeEffect>() != null} cursed {go.GetComponent<OccultCurseEffect>() != null} elite {go.GetComponent<EliteEnemyController>() != null}; {EnemyStatus.Describe()}";
            }
            default: return "unknown command";
        }
    }
}
