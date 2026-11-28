using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2CppEmeraldAI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Coop;

// A client's stand-in target on the host. The plain bridge's TriggerStun is an empty method too short
// for a hook (hooks on short methods crashed this game), so the stand-in is a subclass that overrides
// it: no detour, the engine's own virtual call lands here.
public class CoopTargetBridge : EmeraldGeneralTargetBridge
{
    static bool? _registered;

    public CoopTargetBridge(IntPtr ptr) : base(ptr) { }

    public override void TriggerStun(float StunLength)
    {
        try { HostEnemies.ProxyStunned(Pointer, StunLength); } catch (Exception e) { CoopModule.Warn("stun on a client", e); }
    }

    internal static bool Register()
    {
        if (_registered.HasValue) return _registered.Value;
        try
        {
            Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<CoopTargetBridge>();
            _registered = true;
        }
        catch (Exception e)
        {
            _registered = false;
            Plugin.L.LogWarning($"Co-op enemies: the stand-in with stuns could not be registered, using the plain one (no stuns for clients): {e.Message}");
        }
        return _registered.Value;
    }
}

// Dungeon and forest enemies run by the host (cfg HostEnemies on both sides).
//
// Host: its enemies are the real ones. Ten times a second it reports each one's place, animator state
// and health. Every client on its floor has a stand-in target there (same tag, layer and target bridge
// as the player), so the AI sees, chases and hits clients like the host; a hit on a stand-in is sent
// to that client instead of being applied.
//
// Client: the floor spawns no enemies of its own. Each reported enemy is a copy of the same prefab
// with its AI switched off, moved and animated from the reports. A hit on a copy is sent to the host,
// which applies it to the real enemy with the client's stand-in as the attacker; health and death
// come back in the reports, and the copy then dies through the game's own death (drops, experience).
internal static class HostEnemies
{
    public const byte MsgEnemies = 3, MsgHit = 5, MsgHurt = 6, MsgStun = 17, MsgStunned = 18;
    const int PerPacket = 14;            // about 60 bytes each: stays in one datagram
    const int HeaderCount = 13;          // type, origin, seed, then the count byte
    const float SendInterval = 0.1f, ScanInterval = 0.5f, GhostTimeout = 1.5f, DeadFor = 4f;

    internal static bool On => CoopSettings.On && CoopSettings.HostEnemies.Value;
    // The host says in its state packets whether it runs the enemies: a client with the setting on and
    // a host with it off would otherwise walk an empty dungeon.
    internal static bool HostRuns;
    // A client playing the host's floor.
    internal static bool Client => On && HostRuns && !CoopSettings.IsHost && CoopSettings.FollowHost.Value;

    // ---------------------------------------------------------------- host

    sealed class Tracked
    {
        public EmeraldSystem Es; public EmeraldHealth Health; public RoomBoundEnemy Room;
        public EliteEnemyController Elite;
        public string Name, SpeedName; public float DeadAt = -1f;
    }

    internal static bool IsTracked(int id) => _tracked.ContainsKey(id);
    internal static GameObject TrackedObject(int id) => _tracked.TryGetValue(id, out var t) && t.Es != null ? t.Es.gameObject : null;

    static readonly Dictionary<int, Tracked> _tracked = new();
    static readonly List<int> _drop = new();
    static float _nextSend, _nextScan;
    static bool _announced;

    public static void HostPackets(List<byte[]> into)
    {
        if (!On || !CoopSettings.IsHost || Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + SendInterval;
        int seed = World.Key();
        if (seed == 0) { _tracked.Clear(); return; }
        float now = Time.unscaledTime;
        if (now >= _nextScan)
        {
            _nextScan = now + ScanInterval;
            foreach (var es in UnityEngine.Object.FindObjectsOfType<EmeraldSystem>())
            {
                var go = es.gameObject;
                int id = go.GetInstanceID();
                if (_tracked.TryGetValue(id, out var known))
                {
                    // The spawner makes an enemy an elite a moment after it appears.
                    if (known.Elite == null) known.Elite = go.GetComponent<EliteEnemyController>();
                    continue;
                }
                string name = go.name;
                int cut = name.IndexOf('(');
                if (cut > 0) name = name.Substring(0, cut).TrimEnd();
                var t = new Tracked { Es = es, Health = es.HealthComponent ?? go.GetComponent<EmeraldHealth>(), Room = go.GetComponent<RoomBoundEnemy>(), Elite = go.GetComponent<EliteEnemyController>(), Name = name };
                var anim = es.AIAnimator;
                if (anim != null) t.SpeedName = SpeedParam(anim);
                _tracked[id] = t;
            }
            if (!_announced && _tracked.Count > 0) { _announced = true; Plugin.L.LogInfo($"Co-op enemies: reporting {_tracked.Count} enemies to the clients."); }
        }
        MemoryStream ms = null; BinaryWriter w = null; int n = 0;
        void Flush()
        {
            if (ms == null) return;
            var data = ms.ToArray();
            data[HeaderCount] = (byte)n;
            into.Add(data);
            ms = null; n = 0;
        }
        _drop.Clear();
        foreach (var kv in _tracked)
        {
            var t = kv.Value;
            if (t.Es == null || t.Es.WasCollected) { _drop.Add(kv.Key); continue; }
            var go = t.Es.gameObject;
            int hp = t.Health != null ? t.Health.CurrentHealth : 1, max = t.Health != null ? t.Health.StartingHealth : 1;
            if (hp <= 0)
            {
                // A death is reported for a few seconds so every client sees it, then the corpse is left alone.
                if (t.DeadAt < 0f) t.DeadAt = now;
                else if (now - t.DeadAt > DeadFor) continue;
                hp = 0;
            }
            else if (!go.activeInHierarchy) continue;
            if (ms == null) { w = CoopModule.Begin(MsgEnemies, out ms); w.Write(seed); w.Write((byte)0); }
            var tr = t.Es.transform;
            var anim = t.Es.AIAnimator;
            int hash = 0; float time = 0f, speed = 0f;
            if (anim != null && anim.isActiveAndEnabled && anim.layerCount > 0)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                hash = st.fullPathHash; time = st.normalizedTime - Mathf.Floor(st.normalizedTime);
                if (t.SpeedName != null) speed = anim.GetFloat(t.SpeedName);
            }
            w.Write(kv.Key); w.Write(t.Name);
            w.Write(tr.position.x); w.Write(tr.position.y); w.Write(tr.position.z); w.Write(tr.eulerAngles.y);
            w.Write(hash); w.Write(time); w.Write(speed); w.Write(hp); w.Write(max);
            w.Write(hp > 0 ? EnemyStatus.HostFlags(go) : (byte)0); w.Write(EnemyStatus.EliteByte(t.Elite));
            if (++n >= PerPacket) Flush();
        }
        Flush();
        foreach (int id in _drop) _tracked.Remove(id);
    }

    static string SpeedParam(Animator a)
    {
        foreach (var p in a.parameters)
            if (p.type == AnimatorControllerParameterType.Float && p.name.Contains("Speed")) return p.name;
        return null;
    }

    // A client's weapon hit one of this host's enemies.
    static int _hitsApplied, _noEffect, _pierceLogged;
    public static void HostHit(ulong peer, BinaryReader r)
    {
        int id = r.ReadInt32(), amount = r.ReadInt32(), ragdoll = r.ReadInt32(); byte bits = r.ReadByte();
        bool crit = (bits & 1) != 0, pierce = (bits & 2) != 0;
        if (!On || !_tracked.TryGetValue(id, out var t) || t.Es == null || t.Health == null || t.Health.CurrentHealth <= 0) return;
        _proxies.TryGetValue(peer, out var proxy);
        // The stand-in is the attacker: the enemy turns on the player who hit it.
        int before = t.Health.CurrentHealth;
        // A partner's shield-piercing arrow: the Bows mod here is told so for the length of the blow.
        if (pierce) BowsBridge.Force(true);
        // Not this player's hit: his on-hit skills and run statistics stay out of it.
        Skills.SkillUtil.SyntheticBlow = true;
        try
        {
            t.Health.Damage(amount, proxy != null && proxy.Go != null ? proxy.Go.transform : null, ragdoll, crit);
            // Still stopped by the shield (the block is judged against the attacker in front of it):
            // the same blow from nobody goes through.
            var block = t.Es.AIAnimator;
            if (pierce && t.Health.CurrentHealth == before && block != null && block.GetBool("Blocking"))
            {
                t.Health.Damage(amount, null, ragdoll, crit);
                if (_pierceLogged++ < 6) Plugin.L.LogInfo($"Co-op enemies: a client's arrow went through the shield of {t.Name} as a blow from nobody (Bows mod linked {BowsBridge.Linked}, blocks it lifted so far {BowsBridge.Pierced}), health {before} -> {t.Health.CurrentHealth}.");
            }
        }
        finally { Skills.SkillUtil.SyntheticBlow = false; if (pierce) BowsBridge.Force(false); }
        if (t.Health.CurrentHealth == before && _noEffect++ < 8)
        {
            var an = t.Es.AIAnimator;
            Plugin.L.LogInfo($"Co-op enemies: the hit on {t.Name} changed nothing (immortal {t.Health.Immortal}, blocking {(an != null ? an.GetBool("Blocking").ToString() : "?")}, combat {(an != null ? an.GetBool("Combat State Active").ToString() : "?")}, alive for {t.Es.TimeSinceEnabled:0.0} s, room woken {(t.Room != null ? t.Room.HasAggroed.ToString() : "-")}).");
        }
        if (_hitsApplied++ < 12) Plugin.L.LogInfo($"Co-op enemies: a client hit {t.Name} for {amount}{(crit ? " (crit)" : "")}{(pierce ? " (through the shield)" : "")}, health now {t.Health.CurrentHealth}/{t.Health.StartingHealth}.");
    }

    // A client's weapon stunned one of this host's enemies (the game's own stun roll, made in the client's game).
    static int _stuns;
    public static void HostStun(ulong peer, BinaryReader r)
    {
        int id = r.ReadInt32(); float seconds = r.ReadSingle();
        if (!On || !_tracked.TryGetValue(id, out var t) || t.Es == null || t.Health == null || t.Health.CurrentHealth <= 0) return;
        t.Health.TriggerStun(Mathf.Clamp(seconds, 0f, 10f));
        if (_stuns++ < 12) Plugin.L.LogInfo($"Co-op enemies: a client stunned {t.Name} for {seconds:0.0} s.");
    }

    // The stand-in targets of the clients on this floor (what a mod spawning its own enemies can aim them at).
    static readonly List<Transform> _bodies = new();
    internal static List<Transform> ProxyBodies()
    {
        _bodies.Clear();
        foreach (var p in _proxies.Values) if (p.Go != null) _bodies.Add(p.Go.transform);
        return _bodies;
    }

    // ---- stand-in targets for the clients, on the host ----

    sealed class Proxy { public GameObject Go; public IntPtr Bridge; public float NextWake; public string Name; }

    static readonly Dictionary<ulong, Proxy> _proxies = new();
    static readonly Dictionary<IntPtr, ulong> _byBridge = new();
    static readonly List<ulong> _gone = new();

    static void HostProxies()
    {
        var fpc = FirstPersonController.Instance;
        int seed = World.Key();
        string scene = SceneManager.GetActiveScene().name;
        bool live = On && CoopSettings.IsHost && fpc != null && seed != 0;
        _gone.Clear();
        foreach (var id in _proxies.Keys) _gone.Add(id);
        if (live)
            foreach (var kv in CoopModule.RemotePairs)
            {
                var rem = kv.Value;
                // The client reports the seed of the floor it has built: the same seed is the same floor.
                if (rem.Scene != scene || (scene == "DUNGEON" && rem.Seed != seed) || rem.Down) continue;
                _gone.Remove(kv.Key);
                if (!_proxies.TryGetValue(kv.Key, out var p) || p.Go == null)
                {
                    p = MakeProxy(fpc, rem.Name);
                    if (p == null) continue;
                    _proxies[kv.Key] = p;
                    _byBridge[p.Bridge] = kv.Key;
                    Plugin.L.LogInfo($"Co-op enemies: {rem.Name} can now be seen and attacked by this host's enemies.");
                }
                // The player's own transform sits at the middle of his body, the state packet carries the feet.
                var cc = fpc.characterController;
                float lift = (cc.height * 0.5f - cc.center.y) * fpc.transform.lossyScale.y;
                p.Go.transform.SetPositionAndRotation(rem.Pos + Vector3.up * lift, Quaternion.Euler(0f, rem.Yaw, 0f));
                if (Time.unscaledTime >= p.NextWake)
                {
                    p.NextWake = Time.unscaledTime + 0.4f;
                    WakeRoom(p);
                }
            }
        foreach (var id in _gone)
        {
            var p = _proxies[id];
            _byBridge.Remove(p.Bridge);
            if (p.Go != null) UnityEngine.Object.Destroy(p.Go);
            _proxies.Remove(id);
            Plugin.L.LogInfo($"Co-op enemies: the stand-in target of {p.Name} is removed.");
        }
    }

    static Proxy MakeProxy(FirstPersonController fpc, string name)
    {
        try
        {
            var pg = fpc.gameObject;
            var cc = fpc.characterController;
            var go = new GameObject("FSP_CoopTarget_" + name);
            go.tag = pg.tag; go.layer = pg.layer;
            var col = go.AddComponent<CapsuleCollider>();
            col.center = cc.center; col.height = cc.height; col.radius = cc.radius;
            // Enemies see and hit this collider; the host himself walks through his partner.
            Physics.IgnoreCollision(col, cc, true);
            // The client is hurt by the traps of its own game, which it can see: the host's copies of
            // them (the same traps, swinging out of step) leave its stand-in alone.
            // Switched-off ones too: a plate trap's hurtbox wakes only while the trap goes off.
            foreach (var trap in UnityEngine.Object.FindObjectsOfType<TrapHurtbox>(true))
                foreach (var tc in trap.GetComponentsInChildren<Collider>(true))
                    if (tc != null) Physics.IgnoreCollision(col, tc, true);
            var faction = pg.GetComponent<FactionExtension>();
            if (faction != null) go.AddComponent<FactionExtension>().CurrentFaction = faction.CurrentFaction;
            EmeraldGeneralTargetBridge bridge = CoopTargetBridge.Register() ? go.AddComponent<CoopTargetBridge>() : go.AddComponent<EmeraldGeneralTargetBridge>();
            // Never dies on the host: the client's own game decides that.
            bridge.Immortal = true; bridge.StartingHealth = 1000000; bridge._Health_k__BackingField = 1000000;
            return new Proxy { Go = go, Bridge = bridge.Pointer, Name = name };
        }
        catch (Exception e) { CoopModule.Warn("stand-in target", e); return null; }
    }

    // An enemy's ability stunned a client's stand-in: the client is stunned in its own game.
    static int _stunsOut;
    internal static void ProxyStunned(IntPtr bridge, float seconds)
    {
        if (!_byBridge.TryGetValue(bridge, out ulong peer)) return;
        var w = CoopModule.Begin(MsgStunned, out var ms);
        w.Write(seconds);
        CoopModule.SendTo(peer, ms.ToArray());
        if (_stunsOut++ < 12) Plugin.L.LogInfo($"Co-op enemies: {(_proxies.TryGetValue(peer, out var p) ? p.Name : "a client")} is stunned for {seconds:0.0} s.");
    }

    // dev: what an enemy's stun does to the first stand-in.
    internal static string DevStunProxy(float seconds)
    {
        foreach (var p in _proxies.Values)
        {
            if (p.Go == null) continue;
            var b = p.Go.GetComponent<EmeraldGeneralTargetBridge>();
            b.TriggerStun(seconds);
            return $"TriggerStun({seconds}) on the stand-in of {p.Name} ({b.GetIl2CppType().Name})";
        }
        return "no stand-in";
    }

    // Client: the host says one of its enemies stunned this player.
    static int _stunsIn;
    public static void Stunned(BinaryReader r)
    {
        float seconds = Mathf.Clamp(r.ReadSingle(), 0f, 10f);
        var fpc = FirstPersonController.Instance;
        if (!Client || fpc == null || Revive.Down) return;
        var bridge = fpc.GetComponent<EmeraldGeneralTargetBridgeCustom>();
        if (bridge != null) bridge.TriggerStun(seconds);
        if (_stunsIn++ < 12) Plugin.L.LogInfo($"Co-op enemies: a host enemy stunned you for {seconds:0.0} s.");
    }

    // Room enemies sleep until the host's own player walks in; a client walking in wakes them too.
    static void WakeRoom(Proxy p)
    {
        var pos = p.Go.transform.position;
        foreach (var t in _tracked.Values)
        {
            if (t.Room == null || t.Es == null || t.DeadAt >= 0f) continue;
            try
            {
                if (t.Room.HasAggroed || !t.Room.IsInsideRoomBounds(pos, 0f)) continue;
                t.Room.TriggerRoomAggro(p.Go.transform, "co-op partner entered the room");
            }
            catch (Exception e) { CoopModule.Warn("room wake-up", e); }
        }
    }

    // An enemy's blow landed on a client's stand-in: the client takes it in its own game.
    [HarmonyPatch(typeof(EmeraldGeneralTargetBridge), nameof(EmeraldGeneralTargetBridge.Damage))]
    internal static class ProxyDamagePatch
    {
        static int _logged, _lightning;
        static bool Prefix(EmeraldGeneralTargetBridge __instance, int DamageAmount, Transform AttackerTransform, bool CriticalHit)
        {
            if (!_byBridge.TryGetValue(__instance.Pointer, out ulong peer)) return true;
            try
            {
                int attacker = 0;
                if (AttackerTransform != null)
                {
                    var es = AttackerTransform.GetComponentInParent<EmeraldSystem>();
                    attacker = (es != null ? es.gameObject : AttackerTransform.gameObject).GetInstanceID();
                    // The forest storm strikes around the host. A client has a storm of its own and
                    // cannot see this one: its lightning leaves the stand-in alone.
                    if (es == null && AttackerTransform.name.Contains("Lightning Strike"))
                    {
                        if (_lightning++ < 3) Plugin.L.LogInfo("Co-op enemies: the host's lightning passed through a client's stand-in (each player has his own storm).");
                        return false;
                    }
                }
                var w = CoopModule.Begin(MsgHurt, out var ms);
                w.Write(DamageAmount); w.Write(attacker); w.Write(CriticalHit);
                CoopModule.SendTo(peer, ms.ToArray());
                if (_logged++ < 12) Plugin.L.LogInfo($"Co-op enemies: {(AttackerTransform != null ? AttackerTransform.name : "something")} hit {(_proxies.TryGetValue(peer, out var p) ? p.Name : "a client")} for {DamageAmount}.");
            }
            catch (Exception e) { CoopModule.Warn("hit on a client", e); }
            return false;
        }
    }

    // ---------------------------------------------------------------- client

    sealed class Ghost
    {
        public GameObject Go; public EmeraldSystem Es; public EmeraldHealth Health; public Animator Anim;
        public IntPtr HealthPtr, EsPtr;
        public string Name, SpeedName;
        public Vector3 Pos; public float Yaw, Seen, Speed, Phase;
        public int Hash, Applied, Hp, MaxHp, AppliedHp = int.MinValue, BornFrame;
        public bool Settled, Dead, MyHit;
        public EmeraldSounds Sounds;
        public byte Status, Elite, AppliedElite; public bool FrozenSeen, CursedSeen; public float NextStatus;
    }

    // A living, settled copy of the host's enemy with this id (null when there is none).
    internal static bool Copy(int id, out GameObject go, out EmeraldSounds sounds)
    {
        go = null; sounds = null;
        if (!_ghosts.TryGetValue(id, out var g) || !g.Settled || g.Dead || g.Go == null) return false;
        go = g.Go; sounds = g.Sounds;
        return true;
    }

    // Which of the host's enemies this object (or a part of it) is a copy of; 0 when it is none.
    internal static int CopyId(GameObject part)
    {
        if (part == null) return 0;
        var health = part.GetComponentInParent<EmeraldHealth>();
        return health != null && _byHealth.TryGetValue(health.Pointer, out int id) ? id : 0;
    }

    static readonly Dictionary<int, Ghost> _ghosts = new();
    static readonly Dictionary<IntPtr, int> _byHealth = new();
    static readonly HashSet<IntPtr> _ghostSystems = new();
    static readonly Dictionary<string, GameObject> _prefabs = new();
    static readonly HashSet<string> _missing = new();
    static readonly List<int> _goneGhosts = new();
    static bool _shown, _bypass;
    static float _nextSweep;
    static int _swept, _hitsSent, _hurts;

    internal static int GhostCount => _ghosts.Count;
    internal static int ProxyCount => _proxies.Count;
    internal static int TrackedCount => _tracked.Count;

    public static void Receive(BinaryReader r, bool fromHost)
    {
        if (!fromHost || !Client) return;
        int seed = r.ReadInt32();
        int count = r.ReadByte();
        // Reports of another floor (this client is still loading the host's) are not drawn here.
        if (!World.ClientShares || World.ClientKey != seed) return;
        for (int i = 0; i < count; i++)
        {
            int id = r.ReadInt32(); string name = r.ReadString();
            var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            float yaw = r.ReadSingle(); int hash = r.ReadInt32(); float time = r.ReadSingle(), speed = r.ReadSingle();
            int hp = r.ReadInt32(), max = r.ReadInt32();
            byte status = r.ReadByte(), elite = r.ReadByte();
            if (!_ghosts.TryGetValue(id, out var g))
            {
                if (hp <= 0) continue; // it died before this client ever saw it
                g = Make(name, pos, yaw);
                if (g == null) continue;
                _ghosts[id] = g;
                _byHealth[g.HealthPtr] = id;
                _ghostSystems.Add(g.EsPtr);
                if (!_shown) { _shown = true; Plugin.L.LogInfo($"Co-op enemies: showing the host's enemies (first: {name})."); }
            }
            g.Pos = pos; g.Yaw = yaw; g.Hash = hash; g.Phase = time; g.Speed = speed; g.Hp = hp; g.MaxHp = max; g.Seen = Time.unscaledTime;
            g.Status = status; g.Elite = elite;
        }
    }

    public static void Tick()
    {
        if (!Client || !World.ClientShares) return;
        if (Time.unscaledTime < _nextSweep) return;
        _nextSweep = Time.unscaledTime + 0.5f;
        // Whatever still spawns an enemy here (an event, a boss room): the host's world is the one that counts.
        int n = 0;
        foreach (var es in UnityEngine.Object.FindObjectsOfType<EmeraldSystem>())
        {
            if (_ghostSystems.Contains(es.Pointer)) continue;
            UnityEngine.Object.Destroy(es.gameObject); n++;
        }
        if (n > 0) { _swept += n; Plugin.L.LogInfo($"Co-op enemies: removed {n} enemies of this game's own (the host's are the real ones)."); }
    }

    public static void LateTick()
    {
        HostProxies();
        if (_ghosts.Count == 0) return;
        bool here = Client && World.ClientShares;
        float now = Time.unscaledTime, k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
        _goneGhosts.Clear();
        foreach (var kv in _ghosts)
        {
            var g = kv.Value;
            if (!here || g.Go == null || now - g.Seen > GhostTimeout)
            {
                if (g.Go != null) UnityEngine.Object.Destroy(g.Go);
                _goneGhosts.Add(kv.Key);
                continue;
            }
            if (!g.Settled)
            {
                // Its scripts have had their Awake and Start by the second frame: the AI is switched off after that.
                if (Time.frameCount < g.BornFrame + 2) continue;
                Settle(g);
            }
            if (g.Dead) continue; // the game's own death plays out
            if (g.Hp <= 0) { Die(g); continue; }
            if (g.Elite != g.AppliedElite) { g.AppliedElite = g.Elite; EnemyStatus.MakeElite(g.Go, g.Elite); }
            if ((g.Status != 0 || g.FrozenSeen || g.CursedSeen) && now >= g.NextStatus)
            {
                g.NextStatus = now + 0.3f;
                EnemyStatus.Apply(g.Go, g.Status, ref g.FrozenSeen, ref g.CursedSeen);
            }
            if (g.Hp != g.AppliedHp && g.Health != null)
            {
                bool hurt = g.AppliedHp != int.MinValue && g.Hp < g.AppliedHp;
                g.AppliedHp = g.Hp;
                try
                {
                    g.Health.StartingHealth = g.MaxHp; g.Health.CurrentHealth = g.Hp;
                    if (hurt) g.Health.StartFlash(0.12f);
                }
                catch (Exception e) { CoopModule.Warn("enemy health", e); }
            }
            var t = g.Go.transform;
            t.position = (g.Pos - t.position).sqrMagnitude > 25f ? g.Pos : Vector3.Lerp(t.position, g.Pos, k);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0, g.Yaw, 0), k);
            if (g.Anim == null) continue;
            if (g.SpeedName != null) g.Anim.SetFloat(g.SpeedName, g.Speed);
            // The same controller on both sides: the host's state, entered at the host's point in it.
            if (g.Hash != 0 && g.Hash != g.Applied) { g.Applied = g.Hash; g.Anim.CrossFade(g.Hash, 0.15f, 0, g.Phase); }
        }
        foreach (int id in _goneGhosts)
        {
            var g = _ghosts[id];
            _byHealth.Remove(g.HealthPtr); _ghostSystems.Remove(g.EsPtr);
            _ghosts.Remove(id);
        }
    }

    static Ghost Make(string name, Vector3 pos, float yaw)
    {
        if (_missing.Contains(name)) return null;
        if (!_prefabs.TryGetValue(name, out var prefab) || prefab == null)
        {
            foreach (var es in Resources.FindObjectsOfTypeAll<EmeraldSystem>())
            {
                var go = es.gameObject;
                if (go.name != name || go.scene.IsValid()) continue; // a prefab asset, not an enemy in the scene
                prefab = go; break;
            }
            if (prefab == null) { _missing.Add(name); Plugin.L.LogWarning($"Co-op enemies: no prefab named '{name}' is loaded here."); return null; }
            _prefabs[name] = prefab;
        }
        try
        {
            // It keeps the name the game itself gives a spawned enemy: run statistics and kill lists read it.
            var clone = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
            var es = clone.GetComponent<EmeraldSystem>();
            var health = clone.GetComponent<EmeraldHealth>();
            if (es == null || health == null) { UnityEngine.Object.Destroy(clone); _missing.Add(name); return null; }
            return new Ghost
            {
                Go = clone, Es = es, Health = health, Sounds = clone.GetComponent<EmeraldSounds>(), Name = name, HealthPtr = health.Pointer, EsPtr = es.Pointer,
                Pos = pos, Yaw = yaw, BornFrame = Time.frameCount,
            };
        }
        catch (Exception e) { _missing.Add(name); Plugin.L.LogWarning($"Co-op enemies: copy of '{name}' failed: {e.Message}"); return null; }
    }

    // What stays awake on a copy: its health (the hit target, flash, death) and the hit reaction.
    static bool Keeps(MonoBehaviour c) => c.TryCast<EmeraldHealth>() != null || c.TryCast<HitReaction>() != null;

    static void Settle(Ghost g)
    {
        g.Settled = true;
        try
        {
            foreach (var c in g.Go.GetComponentsInChildren<MonoBehaviour>(true))
                if (c != null && !Keeps(c)) c.enabled = false;
            var agent = g.Go.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            g.Anim = g.Es.AIAnimator != null ? g.Es.AIAnimator : g.Go.GetComponentInChildren<Animator>();
            if (g.Anim != null)
            {
                g.SpeedName = SpeedParam(g.Anim);
                g.Anim.applyRootMotion = false; // the host's reports move it
                g.Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                try { g.Anim.fireEvents = false; } catch { } // attack and sound events belong to the host's enemy
            }
        }
        catch (Exception e) { CoopModule.Warn("enemy copy setup", e); }
    }

    // The host's enemy died: the copy dies through the game's own death, so this player gets its drops.
    static void Die(Ghost g)
    {
        g.Dead = true;
        try
        {
            var fpc = FirstPersonController.Instance;
            if (g.Anim != null) try { g.Anim.fireEvents = true; } catch { }
            EnemyStatus.BeforeDeath(g.Go);
            // One point of health and a blow of one, marked as bookkeeping: the death is real, on-hit
            // skills and damage statistics do not see it.
            g.Health.CurrentHealth = 1;
            _bypass = true; Skills.SkillUtil.SyntheticBlow = true;
            try
            {
                g.Health.Damage(1, g.MyHit && fpc != null ? fpc.transform : null, 100, false);
                // Absorbed: a raised shield stops a blow from the front but not one from nobody.
                if (g.Health.CurrentHealth > 0) g.Health.Damage(1, null, 100, false);
                if (g.Health.CurrentHealth > 0) g.Health.Damage(100000, null, 100, false);
            }
            finally { _bypass = false; Skills.SkillUtil.SyntheticBlow = false; }
        }
        catch (Exception e)
        {
            CoopModule.Warn("enemy death", e);
            if (g.Go != null) UnityEngine.Object.Destroy(g.Go);
        }
    }

    // This player's weapon hit a copy: the host decides what that does. The hook sits on DamageInternal
    // (what Damage and DamageFromTrap both call) and runs last: by then the Bows mod's early-release
    // penalty and the on-hit skills of Fell & Sell Plus have shaped the blow and its run statistics have
    // counted it, exactly as for a hit on an enemy of this player's own.
    [HarmonyPatch(typeof(EmeraldHealth), nameof(EmeraldHealth.DamageInternal))]
    internal static class GhostDamagePatch
    {
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(EmeraldHealth __instance, int DamageAmount, int RagdollForce, bool CriticalHit, bool isTrapDamage)
        {
            if (_bypass || !_byHealth.TryGetValue(__instance.Pointer, out int id)) return true;
            // Traps of this client's floor have no say over the host's enemies.
            if (isTrapDamage) return false;
            try
            {
                if (_ghosts.TryGetValue(id, out var g))
                {
                    if (g.Dead) return false;
                    g.MyHit = true;
                    try { __instance.StartFlash(0.12f); } catch { }
                }
                var w = CoopModule.Begin(MsgHit, out var ms);
                bool pierce = BowsBridge.PiercingNow;
                w.Write(id); w.Write(DamageAmount); w.Write(RagdollForce); w.Write((byte)((CriticalHit ? 1 : 0) | (pierce ? 2 : 0)));
                CoopModule.SendToHost(ms.ToArray());
                if (_hitsSent++ < 12) Plugin.L.LogInfo($"Co-op enemies: your hit for {DamageAmount}{(CriticalHit ? " (crit)" : "")}{(pierce ? " (through the shield)" : "")} on {(g != null ? g.Name : "an enemy")} is sent to the host.");
            }
            catch (Exception e) { CoopModule.Warn("hit on a host enemy", e); }
            return false;
        }
    }

    // This player's weapon stunned a copy: the stun belongs on the host's enemy. (The Bows mod hooks
    // this same method, so the hook is known to be safe.)
    [HarmonyPatch(typeof(EmeraldHealth), nameof(EmeraldHealth.TriggerStun))]
    internal static class GhostStunPatch
    {
        static int _sent;
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(EmeraldHealth __instance, float baseDuration)
        {
            if (!_byHealth.TryGetValue(__instance.Pointer, out int id)) return true;
            try
            {
                var w = CoopModule.Begin(MsgStun, out var ms);
                w.Write(id); w.Write(baseDuration);
                CoopModule.SendToHost(ms.ToArray());
                if (_sent++ < 12) Plugin.L.LogInfo($"Co-op enemies: your stun of {baseDuration:0.0} s is sent to the host.");
            }
            catch (Exception e) { CoopModule.Warn("stun on a host enemy", e); }
            return false;
        }
    }

    // One of the host's enemies hit this client.
    public static void Hurt(BinaryReader r)
    {
        int amount = r.ReadInt32(), attacker = r.ReadInt32(); bool crit = r.ReadBoolean();
        var fpc = FirstPersonController.Instance;
        if (!Client || fpc == null || Revive.Down) return;
        Transform from = _ghosts.TryGetValue(attacker, out var g) && g.Go != null ? g.Go.transform : null;
        // The player's own target bridge: blocking, armour, knockback and death as with any enemy hit.
        var bridge = fpc.GetComponent<EmeraldGeneralTargetBridgeCustom>();
        if (bridge != null) bridge.Damage(amount, from, 100, crit);
        else PlayerStats.Instance.TakeDamage(amount, from);
        if (_hurts++ < 12) Plugin.L.LogInfo($"Co-op enemies: {(g != null ? g.Name : "a host enemy")} hit you for {amount}, health now {PlayerStats.Instance.CurrentHealth:0}.");
    }

    // ---- the client's floor spawns no enemies ----

    static bool SkipOwnSpawn(string what)
    {
        if (!Client || !DungeonSync.BuildingHostFloor) return false;
        Plugin.L.LogInfo($"Co-op enemies: {what} skipped, this floor's enemies are the host's.");
        return true;
    }

    [HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.SpawnEnemiesInDungeon))]
    internal static class NoSpawnPatch { static bool Prefix() => !SkipOwnSpawn("enemy spawn"); }

    [HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.EnsureEnemiesExist))]
    internal static class NoEnsurePatch { static bool Prefix() => !SkipOwnSpawn("enemy check"); }

    [HarmonyPatch(typeof(BossRoomSpawner), nameof(BossRoomSpawner.SpawnEnemiesNow))]
    internal static class NoBossSpawnPatch { static bool Prefix() => !SkipOwnSpawn("boss spawn"); }
}
