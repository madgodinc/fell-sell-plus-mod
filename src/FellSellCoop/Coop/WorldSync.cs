using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Coop;

// Doors, traps and the forest event of a shared place (phase 6).
//
// Doors (DoubleDoorController): a door a player walks through stays open for good in his own game.
// On the host a client's stand-in already opens doors like a player. What was missing is the way
// back: the host lists the doors that stand open for good and a client opens the same ones. A door
// only ever goes from closed to open this way, so two games cannot fight over it. Doors that
// enemies hold open are left to each game's own enemy bodies.
//
// Traps: every player is hurt only by the traps of his own game (the host's trap hurtboxes ignore
// a client's stand-in), so nobody is hit twice. What is shared is when they go off: a pressure
// plate that fires in one game fires in every game on that floor, and spinning blades turn in step
// with the host's.
//
// Forest: a client's forest is set up with the host's event and seed (weather, light, spawn fields).
internal static class WorldSync
{
    public const byte MsgDoors = 20, MsgPlate = 21, MsgSpin = 22, MsgForest = 23;
    const float MatchRadius = 1f;

    // The shared place this player stands in (0 = none): the same number on every player there.
    static int Key => CoopSettings.IsHost ? World.Key() : World.ClientShares ? World.ClientKey : 0;

    static float _nextDoors, _nextSpin, _nextForest, _nextScan, _nextApply;
    static int _scanKey;
    static DoubleDoorController[] _doors = Array.Empty<DoubleDoorController>();
    static PressurePlateTrap[] _plates = Array.Empty<PressurePlateTrap>();
    static WindmillRotator[] _spinners = Array.Empty<WindmillRotator>();
    static readonly Dictionary<IntPtr, float> _plateFired = new();
    static readonly List<Vector3> _hostDoors = new();
    static readonly List<(Vector3 pos, float angle)> _hostSpin = new();
    static int _hostDoorsKey;
    static float _hostDoorsAt = -100f;
    static int _doorsOpened, _platesSent, _platesFired, _spinFixes;

    // What stands on this floor. Looked up again every few seconds: a floor change replaces all of it.
    static void Scan(int key)
    {
        if (key == _scanKey && Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 3f;
        if (key != _scanKey) _plateFired.Clear();
        _scanKey = key;
        _doors = UnityEngine.Object.FindObjectsOfType<DoubleDoorController>();
        _plates = UnityEngine.Object.FindObjectsOfType<PressurePlateTrap>();
        _spinners = UnityEngine.Object.FindObjectsOfType<WindmillRotator>().Where(w => w.GetComponent<TrapHurtbox>() != null).ToArray();
        // A plate seen for the first time counts as not fired since.
        foreach (var p in _plates) if (p != null && !_plateFired.ContainsKey(p.Pointer)) _plateFired[p.Pointer] = p._lastTriggerTime;
    }

    static T Nearest<T>(T[] all, Vector3 pos) where T : Component
    {
        T best = null; float bestD = MatchRadius * MatchRadius;
        foreach (var c in all)
        {
            if (c == null) continue;
            float d = (c.transform.position - pos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    // The angle a spinner has turned about its own axis.
    static float SpinAngle(WindmillRotator w)
    {
        Vector3 e = w.transform.localEulerAngles, ax = w.rotationAxis;
        float x = Mathf.Abs(ax.x), y = Mathf.Abs(ax.y), z = Mathf.Abs(ax.z);
        return y >= x && y >= z ? e.y : x >= z ? e.x : e.z;
    }

    // ---------------------------------------------------------------- every player

    public static void Tick()
    {
        int key = Key;
        if (key == 0 || !CoopModule.HasPartner) return;
        Scan(key);
        if (CoopSettings.ShareTraps.Value) WatchPlates(key);
        if (CoopSettings.IsHost || Time.unscaledTime < _nextApply) return;
        _nextApply = Time.unscaledTime + 0.25f;
        if (Gather.Holding) return;
        try { if (CoopSettings.ShareDoors.Value) ApplyDoors(key); } catch (Exception e) { CoopModule.Warn("doors", e); }
    }

    // A plate that went off here since the last look is told to everybody (the host passes a
    // client's packet on to the other clients).
    static void WatchPlates(int key)
    {
        foreach (var p in _plates)
        {
            if (p == null) continue;
            float last = p._lastTriggerTime;
            if (!_plateFired.TryGetValue(p.Pointer, out float known)) { _plateFired[p.Pointer] = last; continue; }
            if (last <= known + 0.01f) continue;
            _plateFired[p.Pointer] = last;
            Vector3 pos = p.transform.position;
            var w = CoopModule.Begin(MsgPlate, out var ms);
            w.Write(key); w.Write(pos.x); w.Write(pos.y); w.Write(pos.z);
            CoopModule.SendAll(ms.ToArray());
            if (_platesSent++ < 10) Plugin.L.LogInfo($"Co-op traps: {p.name} at {pos} went off here, telling the others.");
        }
    }

    public static void ReceivePlate(BinaryReader r)
    {
        int key = r.ReadInt32();
        var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        if (!CoopSettings.ShareTraps.Value || key == 0 || key != Key) return;
        Scan(key);
        var p = Nearest(_plates, pos);
        if (p == null) return;
        // Already going off here (the same step set off both copies): nothing to add.
        if (p._isAnimating || Time.time - p._lastTriggerTime < 0.75f) return;
        p.FireTrap();
        p._lastTriggerTime = Time.time;
        _plateFired[p.Pointer] = p._lastTriggerTime;
        if (_platesFired++ < 10) Plugin.L.LogInfo($"Co-op traps: {p.name} at {p.transform.position} went off in a partner's game, it goes off here too.");
    }

    // ---------------------------------------------------------------- host

    public static void HostPackets(List<byte[]> into)
    {
        float now = Time.unscaledTime;
        if (now >= _nextForest) { _nextForest = now + 2f; try { ForestPacket(into); } catch (Exception e) { CoopModule.Warn("forest event", e); } }
        int key = World.Key();
        if (key == 0) return;
        Scan(key);
        if (now >= _nextDoors && CoopSettings.ShareDoors.Value)
        {
            _nextDoors = now + 0.5f;
            var w = CoopModule.Begin(MsgDoors, out var ms);
            var open = new List<Vector3>();
            foreach (var d in _doors) if (d != null && d.IsPermanentlyOpen && open.Count < 100) open.Add(d.transform.position);
            w.Write(key); w.Write((byte)open.Count);
            foreach (var p in open) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
            into.Add(ms.ToArray());
        }
        if (now >= _nextSpin && CoopSettings.ShareTraps.Value && _spinners.Length > 0)
        {
            _nextSpin = now + 1f;
            var w = CoopModule.Begin(MsgSpin, out var ms);
            int n = Mathf.Min(_spinners.Count(s => s != null), 80);
            w.Write(key); w.Write((byte)n);
            foreach (var s in _spinners)
            {
                if (s == null || n-- <= 0) continue;
                Vector3 p = s.transform.position;
                w.Write(p.x); w.Write(p.y); w.Write(p.z); w.Write(SpinAngle(s));
            }
            into.Add(ms.ToArray());
        }
    }

    // ---------------------------------------------------------------- client

    public static void ReceiveDoors(BinaryReader r)
    {
        _hostDoorsKey = r.ReadInt32();
        int n = r.ReadByte();
        _hostDoors.Clear();
        for (int i = 0; i < n; i++) _hostDoors.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
        _hostDoorsAt = Time.unscaledTime;
    }

    // Applied at once: the blades turn fast, a report that waited would be stale.
    public static void ReceiveSpin(BinaryReader r)
    {
        int key = r.ReadInt32();
        int n = r.ReadByte();
        _hostSpin.Clear();
        for (int i = 0; i < n; i++) _hostSpin.Add((new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadSingle()));
        if (!CoopSettings.ShareTraps.Value || key == 0 || key != Key || Gather.Holding) return;
        Scan(key);
        foreach (var (pos, angle) in _hostSpin)
        {
            var s = Nearest(_spinners, pos);
            if (s == null) continue;
            float off = Mathf.DeltaAngle(SpinAngle(s), angle);
            if (Mathf.Abs(off) < 25f) continue;
            s.transform.Rotate(s.rotationAxis.normalized * off, Space.Self);
            _spinFixes++;
        }
    }

    static void ApplyDoors(int key)
    {
        if (_hostDoorsKey != key || Time.unscaledTime - _hostDoorsAt > 3f) return;
        foreach (var pos in _hostDoors)
        {
            var d = Nearest(_doors, pos);
            if (d == null || d.IsPermanentlyOpen) continue;
            // The leaves swing away from whoever opened them: the partner standing nearest to the door.
            Vector3 from = pos; float best = float.MaxValue;
            foreach (var rem in CoopModule.Remotes)
            {
                float dist = (rem.Pos - pos).sqrMagnitude;
                if (rem.Scene == SceneManager.GetActiveScene().name && dist < best) { best = dist; from = rem.Pos; }
            }
            if (!d.isOpen) d.OpenDoor(from);
            d.openedByPlayer = true;
            if (_doorsOpened++ < 20) Plugin.L.LogInfo($"Co-op doors: the door at {pos} stands open on the host, opening it here.");
        }
    }

    // ---------------------------------------------------------------- forest event

    static string _hostEvent = ""; static int _hostEventSeed; static float _hostEventLeft, _hostEventAt = -100f;
    static string _sceneEvent = ""; static int _sceneEventSeed; // what this game's forest was last set up with

    // The event the host's forest shows: the one its scene was set up with while it is there,
    // otherwise the one a visit would start with.
    static void ForestPacket(List<byte[]> into)
    {
        var m = ForestEventManager.Instance;
        if (m == null || !CoopSettings.ShareForestEvent.Value) return;
        bool there = SceneManager.GetActiveScene().name == "FOREST" && _sceneEvent.Length > 0;
        var w = CoopModule.Begin(MsgForest, out var ms);
        w.Write(there ? _sceneEvent : m.currentEventName ?? ""); w.Write(there ? _sceneEventSeed : m.currentEventSeed); w.Write(m.timeRemaining);
        into.Add(ms.ToArray());
    }

    public static void ReceiveForest(BinaryReader r)
    {
        _hostEvent = r.ReadString(); _hostEventSeed = r.ReadInt32(); _hostEventLeft = r.ReadSingle();
        _hostEventAt = Time.unscaledTime;
    }

    [HarmonyLib.HarmonyPatch(typeof(ForestEventManager), nameof(ForestEventManager.SetupForestScene))]
    internal static class ForestSetupPatch
    {
        static void Prefix(ForestEventManager __instance)
        {
            try
            {
                if (!CoopSettings.On || CoopSettings.IsHost || !CoopSettings.ShareForestEvent.Value || !CoopSettings.FollowHost.Value) return;
                if (_hostEvent.Length == 0 || Time.unscaledTime - _hostEventAt > 6f) return;
                if (__instance.currentEventName == _hostEvent && __instance.currentEventSeed == _hostEventSeed) return;
                Plugin.L.LogInfo($"Co-op forest: taking the host's event '{_hostEvent}' (seed {_hostEventSeed}) instead of '{__instance.currentEventName}'.");
                __instance.currentEventName = _hostEvent; __instance.currentEventSeed = _hostEventSeed; __instance.timeRemaining = _hostEventLeft;
            }
            catch (Exception e) { CoopModule.Warn("forest event", e); }
        }

        static void Postfix(ForestEventManager __instance)
        {
            _sceneEvent = __instance.currentEventName ?? ""; _sceneEventSeed = __instance.currentEventSeed;
        }
    }

    // ---------------------------------------------------------------- dev

    static string Types(GameObject go)
    {
        var names = new List<string>();
        foreach (var c in go.GetComponents<Component>()) if (c != null) names.Add(c.GetIl2CppType().Name);
        return string.Join(",", names);
    }

    static string Path(Transform t, int up)
    {
        var parts = new List<string>();
        for (int i = 0; i <= up && t != null; i++, t = t.parent) parts.Add($"{t.name}[{Types(t.gameObject)}]");
        return string.Join(" < ", parts);
    }

    internal static string Dev(string[] a)
    {
        var fpc = FirstPersonController.Instance;
        var sb = new StringBuilder();
        Vector3 me = fpc != null ? fpc.transform.position : Vector3.zero;
        switch (a[0])
        {
            case "doors":
            {
                var all = UnityEngine.Object.FindObjectsOfType<DoubleDoorController>().OrderBy(d => (d.transform.position - me).sqrMagnitude).ToList();
                if (a.Length > 1 && all.Count > 0)
                {
                    var d = all[0];
                    if (a[1] == "open") d.OpenDoor(me);
                    if (a[1] == "close") d.CloseDoor();
                    if (a[1] == "off") d.enabled = false;
                    if (a[1] == "on") d.enabled = true;
                    if (a[1] == "perm") d.openedByPlayer = true;
                }
                int doorObjs = UnityEngine.Object.FindObjectsOfType<Door>().Length;
                sb.AppendLine($"double doors {all.Count} (open for good {all.Count(d => d.IsPermanentlyOpen)}, open {all.Count(d => d.isOpen)}), Door objects {doorObjs}, host lists {_hostDoors.Count} ({Time.unscaledTime - _hostDoorsAt:0.0} s old), opened here for the host {_doorsOpened}");
                foreach (var d in all.Take(4))
                {
                    sb.AppendLine($"{d.name} at {d.transform.position} {Mathf.Sqrt((d.transform.position - me).sqrMagnitude):0.0} m open {d.isOpen} byPlayer {d.openedByPlayer} enabled {d.enabled} speed {d.moveSpeed} angle {d.openAngle}");
                    try { sb.AppendLine($"  perm {d.IsPermanentlyOpen} occupants {(d._occupants != null ? d._occupants.Count : -1)}"); } catch (Exception e) { sb.AppendLine("  perm/occupants: " + e.Message); }
                    if (d.leftDoorPivot != null) sb.AppendLine($"  left local {d.leftDoorPivot.localRotation.eulerAngles} world {d.leftDoorPivot.rotation.eulerAngles} closed {d.leftClosedRotation.eulerAngles} target {d.leftTargetRotation.eulerAngles}");
                    else sb.AppendLine("  no left pivot");
                    foreach (var col in d.GetComponentsInChildren<Collider>(true))
                        sb.AppendLine($"  collider {col.name} {col.GetIl2CppType().Name} trigger {col.isTrigger} size {col.bounds.size} center {col.bounds.center} rb {col.attachedRigidbody != null}");
                    sb.AppendLine("  " + Path(d.transform, 2));
                }
                foreach (var d in UnityEngine.Object.FindObjectsOfType<Door>().OrderBy(d => (d.transform.position - me).sqrMagnitude).Take(4))
                    sb.AppendLine($"Door {d.name} at {d.transform.position} open {d._isOpen} shop {d._isMainShopDoor}");
                return sb.ToString().TrimEnd();
            }
            case "traps":
            {
                var spikes = UnityEngine.Object.FindObjectsOfType<Il2CppFellAndSell.Traps.SpikeTrap>();
                var plates = UnityEngine.Object.FindObjectsOfType<PressurePlateTrap>();
                var boxes = UnityEngine.Object.FindObjectsOfType<TrapHurtbox>();
                if (a.Length > 1 && a[1] == "step" && plates.Length > 0) // what a player's foot does to the nearest plate
                {
                    var near = plates.OrderBy(p => (p.transform.position - me).sqrMagnitude).First();
                    near.OnTriggerEnter(fpc.characterController.Cast<Collider>());
                }
                if (a.Length > 1 && (a[1] == "go" || a[1] == "look") && plates.Length > 0)
                {
                    // go: onto the nearest plate, as a walking player would; look [m]: a few metres from it, facing it
                    var near = plates.OrderBy(p => (p.transform.position - me).sqrMagnitude).First();
                    Vector3 at = near.transform.position;
                    foreach (var col in near.GetComponents<Collider>()) if (col.isTrigger) at = col.bounds.center;
                    if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var hit, 8f, LayerMask.GetMask("Default", "Terrain", "Floor"), QueryTriggerInteraction.Ignore)) at = hit.point;
                    if (a[1] == "go") CoopDev.Put(fpc, at, at + fpc.transform.forward);
                    else if (CoopDev.SafeSpot(at, me - at, a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 4f, out var spot)) CoopDev.Put(fpc, spot, at);
                    me = fpc.transform.position;
                }
                sb.AppendLine($"spike traps {spikes.Length}, pressure plates {plates.Length}, hurtboxes {boxes.Length}; plates told {_platesSent}, set off for a partner {_platesFired}, spinner corrections {_spinFixes}, hp {PlayerStats.Instance.CurrentHealth:0}");
                foreach (var s in spikes.OrderBy(s => (s.transform.position - me).sqrMagnitude).Take(3))
                    sb.AppendLine($"spike {s.name} at {s.transform.position} local {s.transform.localPosition} state {s._currentState} timer {s._timer:0.00} bottom {s._bottomPosition} top {s._topPosition} speed {s._speed} pauses {s._pauseAtTopDuration}/{s._pauseAtBottomDuration} delay {s._initialDelay} | {Path(s.transform, 2)}");
                foreach (var p in plates.OrderBy(p => (p.transform.position - me).sqrMagnitude).Take(3))
                    sb.AppendLine($"plate {p.name} at {p.transform.position} last {p._lastTriggerTime:0.0} now {Time.time:0.0} animating {p._isAnimating} cooldown {p.cooldown} motion {p.motionType} tags {(p.triggerTags != null ? p.triggerTags.Count : 0)} vfxbox {(p.vfxHurtbox != null ? p.vfxHurtbox.name : "-")} | {Path(p.transform, 2)}");
                var rot = UnityEngine.Object.FindObjectsOfType<WindmillRotator>();
                sb.AppendLine($"rotators {rot.Length}, simple rotators {UnityEngine.Object.FindObjectsOfType<Generic_SimpleRotate>().Length}");
                foreach (var w in rot.OrderBy(w => (w.transform.position - me).sqrMagnitude).Take(3))
                    sb.AppendLine($"rotator {w.name} at {w.transform.position} speed {w.rotationSpeed} axis {w.rotationAxis} local euler {w.transform.localEulerAngles} accumulated {w._accumulatedRotation:0.0} hurtbox {w.GetComponent<TrapHurtbox>() != null}");
                var seen = new HashSet<string>();
                foreach (var b in boxes)
                {
                    string path = Path(b.transform, 3);
                    if (!seen.Add(path) || seen.Count > 10) continue;
                    var anim = b.GetComponentInParent<Animator>();
                    sb.AppendLine($"hurtbox dmg {b.damage} animator {(anim != null ? anim.name : "-")} | {path}");
                }
                return sb.ToString().TrimEnd();
            }
            case "forest":
            {
                var m = ForestEventManager.Instance;
                if (m == null) return "no ForestEventManager";
                if (a.Length > 1 && a[1] == "roll") m.RollNewEvent();
                if (a.Length > 1 && a[1] == "setup") m.SetupForestScene();
                sb.AppendLine($"scene set up with '{_sceneEvent}' seed {_sceneEventSeed}; host's event '{_hostEvent}' seed {_hostEventSeed} left {_hostEventLeft:0} ({Time.unscaledTime - _hostEventAt:0.0} s old)");
                sb.AppendLine($"event '{m.currentEventName}' seed {m.currentEventSeed} left {m.timeRemaining:0} of {m.eventDurationSeconds:0} trip {m.currentEventTripStrength:0.00} scene {SceneManager.GetActiveScene().name}");
                if (m.availableEvents != null)
                    for (int i = 0; i < m.availableEvents.Count; i++)
                    {
                        var e = m.availableEvents[i];
                        sb.AppendLine($"  '{e.eventObjectName}' weight {e.weight} dark {e.disableDirectionalLight} trip {e.causesTripEffect} dot {e.causesDamageOverTime} chest {e.randomChestSpawnChance} fields {(e.spawnFields != null ? e.spawnFields.Count : 0)}");
                    }
                sb.AppendLine($"thunder spawners {UnityEngine.Object.FindObjectsOfType<ThunderstormSpawner>().Length}, weather controls {UnityEngine.Object.FindObjectsOfType<WeatherControl>().Length}, tree runes {UnityEngine.Object.FindObjectsOfType<TreeRuneEvent>().Length}, forest chests {UnityEngine.Object.FindObjectsOfType<ForestChest>().Length}, signposts {UnityEngine.Object.FindObjectsOfType<ForestEventSignpost>().Length}");
                foreach (var w in UnityEngine.Object.FindObjectsOfType<WeatherControl>()) sb.AppendLine($"  weather {w.name} wind {w.windIntensity} weather {w.weatherIntensity}");
                foreach (var t in UnityEngine.Object.FindObjectsOfType<ThunderstormSpawner>()) sb.AppendLine($"  thunder {t.name} delay {t._minSpawnDelay}-{t._maxSpawnDelay} radius {t._minSpawnRadius}-{t._maxSpawnRadius} | {Path(t.transform, 2)}");
                return sb.ToString().TrimEnd();
            }
            case "prayers":
            {
                var pm = PrayersManager.Instance;
                if (pm == null) return "no PrayersManager";
                for (int i = 0; i < pm.allPrayersDatabase.Count; i++)
                {
                    var p = pm.allPrayersDatabase[i];
                    var f = new List<string>();
                    if (p.disarmsTraps) f.Add("disarmsTraps");
                    if (p.enemySpawnMultiplier != 0f && p.enemySpawnMultiplier != 1f) f.Add("enemySpawn x" + p.enemySpawnMultiplier);
                    if (p.isGhost) f.Add("ghost");
                    if (p.isManiac) f.Add("maniac");
                    if (p.vampiricAura) f.Add("vampiricAura");
                    if (p.thornsDamage != 0f || p.thornsShieldArmorPercent != 0f) f.Add("thorns");
                    if (p.midasTouch) f.Add("midas");
                    if (p.devilishPact) f.Add("devilishPact");
                    if (p.playerSizeScale != 0f && p.playerSizeScale != 1f) f.Add("size x" + p.playerSizeScale);
                    if (p.dropChanceBonus != 0f) f.Add("drop +" + p.dropChanceBonus);
                    if (p.lifeDrainPerHit != 0 || p.lifeDrainPercent != 0f) f.Add("lifeDrain");
                    if (p.holyChampion) f.Add("holyChampion");
                    if (p.unholyChampion) f.Add("unholyChampion");
                    if (p.bloodIsPower) f.Add("bloodIsPower");
                    if (f.Count > 0) sb.AppendLine($"{p.name}{(p.isEvil ? " (evil)" : "")}: {string.Join(", ", f)}");
                }
                sb.AppendLine($"active {pm.activePrayers.Count}: disarms {pm.DisarmsTraps} ghost {pm.IsGhost} spawn x{pm.EnemySpawnMultiplier} thorns {pm.ThornsDamage} aura {pm.VampiricAuraCount}");
                return sb.ToString().TrimEnd();
            }
        }
        return null;
    }
}
