using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Coop;

// What a player's body is doing, as it travels in every state packet. Mirrors the Character mod's
// PuppetState without naming its types, so this mod loads when that one is absent.
internal struct BodyState
{
    public float MoveX, MoveY, Pitch, Bow, ActionDuration, ActionSkip;
    public bool Grounded, Block, Crouch, Down, RunsEnemies, FloorOpen, Follows, Joins, Horde;
    public byte Action, ActionSeq;
}

// Co-op prototype: every player sends its scene, position, facing and body state 20 times a second and
// its look every 2 seconds; the others draw it when they are in the same scene, as a Character-mod
// puppet wearing that look, or as a villager-shaped avatar when either side lacks that mod.
// The host relays clients to each other and its dungeon seed and floor lead the clients (DungeonSync).
// On top of that: what each player holds (MsgHeld), the host's enemies (HostEnemies) and death and
// revive (Revive).
internal static class CoopModule
{
    internal const byte MsgInfo = 19; // the host's difficulty, every couple of seconds
    const byte MsgState = 1, MsgLook = 2, MsgHeld = 4; // 3, 5, 6: HostEnemies; 7: Revive; 8, 9: HostEffects; 10, 11: RoomSync; 13: EnemySounds; 14: EnemyStatus; 15, 16: HordeBridge; 17, 18: HostEnemies (stuns); 20-23: WorldSync
    const float SendInterval = 0.05f, LookInterval = 2f, Timeout = 5f;
    const int MaxLookBytes = 1200; // one UDP datagram below the usual MTU, no fragments

    static ITransport _net;
    static bool _failed, _firstSent;
    static float _nextSend, _nextLook, _nextWarm, _nextHeld, _heldSent;
    static byte[] _held;
    static readonly List<(ulong peer, byte[] data)> _inbox = new();
    static readonly List<byte[]> _extra = new();
    static readonly Dictionary<ulong, Remote> _remotes = new();
    internal static byte FakeAction, FakeSeq; // dev: a one-shot action sent without pressing anything
    internal static Vector2 FakeMove;         // dev: the velocity of a scripted walk (the controller reports none for it)

    internal sealed class Remote
    {
        public string Name = "?", Scene = "";
        public Vector3 Pos; public float Yaw, LastSeen, FirstSeen;
        public int Seed, Floor; public bool IsHost;
        public BodyState Body;
        public byte[] Look; public bool LookDirty;
        public byte[] Held; public bool HeldDirty;
        public bool Down => Body.Down;
        public object Puppet; public bool PuppetFailed;
        public GameObject Avatar; public Animator Anim; public Transform Tag;
        public bool Fallback; public float NextUpgrade; public string AvatarScene;
        public Transform Root;
        public int Packets;
    }

    internal static IEnumerable<Remote> Remotes => _remotes.Values;
    internal static IEnumerable<KeyValuePair<ulong, Remote>> RemotePairs => _remotes;
    internal static bool Running => _net != null;
    internal static bool HasPartner => _remotes.Count > 0;

    // Every packet starts with its type and the peer it came from (0 = the sender itself; the host
    // fills it in when passing a client's packet on).
    internal static BinaryWriter Begin(byte type, out MemoryStream ms)
    {
        ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(type); w.Write(0UL);
        return w;
    }

    internal static void SendTo(ulong peer, byte[] pkt) { if (_net != null) _net.Send(peer, pkt); }
    internal static void SendAll(byte[] pkt) { if (_net != null) foreach (var peer in _net.KnownPeers.ToList()) _net.Send(peer, pkt); }
    // A client's only peer is the host.
    internal static void SendToHost(byte[] pkt) { if (!CoopSettings.IsHost) SendAll(pkt); }

    public static void Tick()
    {
        if (_failed || !CoopSettings.On) return;
        // Start only once the player exists: Steam is initialised by then, and game singletons are
        // not touched on the splash screen.
        if (_net == null && (FirstPersonController.Instance == null || !Start())) return;

        SteamLobby.Tick();
        HordeBridge.Tick();
        _inbox.Clear();
        _net.Poll(_inbox);
        foreach (var (peer, data) in _inbox) Receive(peer, data);

        var fpc = FirstPersonController.Instance;
        if (fpc != null && Time.unscaledTime >= _nextSend)
        {
            _nextSend = Time.unscaledTime + SendInterval;
            var pkt = PackState(fpc);
            var peers = _net.KnownPeers.ToList();
            foreach (var peer in peers) _net.Send(peer, pkt);
            if (!_firstSent && peers.Count > 0)
            {
                _firstSent = true;
                Plugin.L.LogInfo($"Co-op: first state packet sent ({pkt.Length} bytes) to {string.Join(", ", peers.Select(PeerName))}.");
            }
            if (Time.unscaledTime >= _nextLook && peers.Count > 0)
            {
                _nextLook = Time.unscaledTime + LookInterval;
                var look = LocalLookPacket();
                if (look != null) foreach (var peer in peers) _net.Send(peer, look);
                if (CoopSettings.IsHost)
                {
                    byte[] info = null;
                    try { info = DungeonSync.DifficultyPacket(); } catch (Exception e) { Warn("difficulty info", e); }
                    if (info != null) foreach (var peer in peers) _net.Send(peer, info);
                }
            }
            // What the hands hold: at once when it changes, and every few seconds for late joiners and lost packets.
            if (Time.unscaledTime >= _nextHeld && peers.Count > 0 && CharacterBridge.Present)
            {
                _nextHeld = Time.unscaledTime + 0.25f;
                byte[] held = null;
                try { held = CharacterBridge.LocalHeld(); } catch (Exception e) { Warn("held items", e); }
                if (held != null && held.Length <= MaxLookBytes)
                {
                    bool changed = _held == null || !_held.AsSpan().SequenceEqual(held);
                    if (changed || Time.unscaledTime - _heldSent >= LookInterval)
                    {
                        if (_held == null) Plugin.L.LogInfo($"Co-op: sending what your hands hold ({held.Length} bytes).");
                        _held = held; _heldSent = Time.unscaledTime;
                        var heldPkt = Blob(MsgHeld, held);
                        foreach (var peer in peers) _net.Send(peer, heldPkt);
                    }
                }
            }
        }
        if (CoopSettings.IsHost)
        {
            _extra.Clear();
            HostEnemies.HostPackets(_extra);
            WorldSync.HostPackets(_extra);
            if (HostEnemies.On) { HostEffects.HostPackets(_extra, World.Key()); RoomSync.HostPackets(_extra); EnemySounds.HostPackets(_extra, World.Key()); }
            if (_extra.Count > 0)
                foreach (var peer in _net.KnownPeers.ToList())
                    foreach (var pkt in _extra) _net.Send(peer, pkt);
        }
        HostEnemies.Tick();
        RoomSync.Tick();
        try { WorldSync.Tick(); } catch (Exception e) { Warn("world sync", e); }
        Revive.Tick();
        CoopPause.Tick();
        // Villagers live only in the village: take the avatar template while there, for partners without a look.
        if (Time.unscaledTime >= _nextWarm)
        {
            _nextWarm = Time.unscaledTime + 3f;
            if (SceneManager.GetActiveScene().name == "MAIN") Avatars.TemplateReady();
        }
        DungeonSync.Tick();
        Gather.Tick();
        CoopHud.Tick();
    }

    static bool Start()
    {
        try
        {
            bool udp = CoopSettings.Udp;
            if (udp) _net = new UdpTransport(CoopSettings.IsHost, CoopSettings.HostAddress.Value, CoopSettings.PortValue);
            else
            {
                var ids = CoopSettings.SteamIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ulong.Parse).ToList();
                var steam = new SteamTransport(ids);
                _net = steam;
                // Nobody listed: the players find each other through a Steam lobby.
                if (ids.Count == 0) SteamLobby.Start(steam, CoopSettings.IsHost);
            }
            string link = !udp ? "Steam P2P" : CoopSettings.IsHost ? $"listening on UDP {CoopSettings.PortValue}" : $"host {CoopSettings.HostAddress.Value}:{CoopSettings.PortValue}";
            string bodies = CharacterBridge.Present ? "Character mod puppets" : "villager avatars (Character mod not installed)";
            // Both games must keep running whichever window has the focus.
            Application.runInBackground = true;
            Plugin.L.LogInfo($"Co-op started: {CoopSettings.RoleValue} over {(udp ? "udp" : "steam")}, name {CoopSettings.Name}, {link}, player bodies: {bodies}.");
            return true;
        }
        catch (Exception e) { _failed = true; Plugin.L.LogError($"Co-op disabled: {e.Message}"); return false; }
    }

    // Ends the session (the player picked another role in the main menu, or single player).
    internal static void Stop()
    {
        if (_net == null) { _failed = false; return; }
        foreach (var rem in _remotes.Values) DestroyBody(rem);
        _remotes.Clear(); _heard.Clear();
        try { _net.Dispose(); } catch { }
        _net = null; _failed = false; _firstSent = false; _held = null;
        SteamLobby.Detach();
        Plugin.L.LogInfo("Co-op stopped.");
    }

    static string PeerName(ulong peer) => _net is UdpTransport ? UdpTransport.Describe(peer) : peer.ToString();

    // ---- sending ----

    // State packet: type, origin peer (0 = the sender itself), name, scene, feet position, yaw, dungeon seed
    // and floor, then the body state. Every packet starts with type and origin so the host can relay any kind.
    static byte[] PackState(FirstPersonController fpc)
    {
        var body = new BodyState { Grounded = true, Bow = -1f };
        var cc = fpc.characterController;
        var pt = fpc.transform;
        Vector3 feet = pt.position + Vector3.up * ((cc.center.y - cc.height * 0.5f) * pt.lossyScale.y);
        bool full = false;
        if (CharacterBridge.Present)
            try { full = CharacterBridge.LocalState(ref body); } catch (Exception e) { Warn("local body state", e); }
        if (!full)
        {
            Vector3 v = Quaternion.Inverse(Quaternion.Euler(0f, pt.eulerAngles.y, 0f)) * cc.velocity;
            body.MoveX = v.x; body.MoveY = v.z;
            body.Grounded = fpc.IsGrounded; body.Pitch = fpc.verticalRotation; body.Crouch = fpc.IsCrouching;
        }
        if (FakeMove != Vector2.zero) { body.MoveX = FakeMove.x; body.MoveY = FakeMove.y; }
        if (FakeSeq != 0) { body.Action = FakeAction; body.ActionSeq = (byte)(body.ActionSeq + FakeSeq); body.ActionDuration = 0.8f; body.ActionSkip = 0.3f; }
        DungeonSync.Current(out int seed, out int floor);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(MsgState); w.Write(0UL); w.Write(CoopSettings.Name); w.Write(SceneManager.GetActiveScene().name);
        w.Write(feet.x); w.Write(feet.y); w.Write(feet.z); w.Write(pt.eulerAngles.y);
        w.Write(seed); w.Write(floor); w.Write(seed != 0 ? DungeonSync.BossRoom : "");
        w.Write(body.MoveX); w.Write(body.MoveY); w.Write(body.Pitch);
        w.Write((byte)((body.Grounded ? 1 : 0) | (body.Block ? 2 : 0) | (body.Crouch ? 4 : 0) | (body.Bow >= 0f ? 8 : 0) | (Revive.Down ? 16 : 0) | (CoopSettings.IsHost && HostEnemies.On ? 32 : 0) | (Gather.Open ? 64 : 0) | (!CoopSettings.IsHost && CoopSettings.FollowHost.Value ? 128 : 0)));
        w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(body.Bow) * 255f));
        w.Write(body.Action); w.Write(body.ActionSeq); w.Write(body.ActionDuration); w.Write(body.ActionSkip);
        w.Write((byte)((DungeonSync.JoinsRuns ? 1 : 0) | (seed != 0 && HordeBridge.Active ? 2 : 0)));
        return ms.ToArray();
    }

    static bool _lookTooBig;
    static int _lookLogged = -1;
    static byte[] LocalLookPacket()
    {
        if (!CharacterBridge.Present) return null;
        byte[] look;
        try { look = CharacterBridge.LocalLook(); } catch (Exception e) { Warn("local look", e); return null; }
        if (look == null) return null;
        if (look.Length > MaxLookBytes)
        {
            if (!_lookTooBig) { _lookTooBig = true; Plugin.L.LogWarning($"Co-op: your look is {look.Length} bytes, over the {MaxLookBytes} byte packet limit; the others see a villager."); }
            return null;
        }
        if (_lookLogged != look.Length) { _lookLogged = look.Length; Plugin.L.LogInfo($"Co-op: sending your look ({look.Length} bytes, every {LookInterval:0} s)."); }
        return Blob(MsgLook, look);
    }

    // Type, origin, a 16-bit length and the bytes.
    static byte[] Blob(byte type, byte[] data)
    {
        var pkt = new byte[11 + data.Length];
        pkt[0] = type;
        pkt[9] = (byte)(data.Length & 0xFF); pkt[10] = (byte)(data.Length >> 8);
        Buffer.BlockCopy(data, 0, pkt, 11, data.Length);
        return pkt;
    }

    // ---- receiving ----

    static readonly HashSet<ulong> _heard = new();

    static void Receive(ulong from, byte[] data)
    {
        try
        {
            if (data.Length < 9) return;
            if (_heard.Add(from)) Plugin.L.LogInfo($"Co-op: first packet received from {PeerName(from)} ({data.Length} bytes, type {data[0]}).");
            ulong origin = BitConverter.ToUInt64(data, 1);
            ulong id = origin != 0 ? origin : from;
            bool fromHost = !CoopSettings.IsHost && origin == 0;

            // The host passes a client's packets on to the other clients, stamped with where they came from
            // (not the hits on its enemies: those are for the host alone).
            if (CoopSettings.IsHost && origin == 0 && data[0] != HostEnemies.MsgHit && data[0] != RoomSync.MsgAsk && data[0] != EnemyStatus.Msg
                && data[0] != HordeBridge.MsgToHost && data[0] != HostEnemies.MsgStun)
            {
                byte[] relay = null;
                foreach (var peer in _net.KnownPeers)
                {
                    if (peer == from) continue;
                    if (relay == null) { relay = (byte[])data.Clone(); BitConverter.GetBytes(from).CopyTo(relay, 1); }
                    _net.Send(peer, relay);
                }
            }

            using var r = new BinaryReader(new MemoryStream(data));
            byte type = r.ReadByte();
            r.ReadUInt64();
            if (type == HostEnemies.MsgEnemies) { HostEnemies.Receive(r, fromHost); return; }
            if (type == HostEnemies.MsgHit) { if (CoopSettings.IsHost) HostEnemies.HostHit(id, r); return; }
            if (type == HostEnemies.MsgHurt) { if (fromHost) HostEnemies.Hurt(r); return; }
            if (type == Revive.Msg) { Revive.Receive(r); return; }
            if (type == HostEffects.MsgState) { if (fromHost && HostEnemies.Client) HostEffects.ReceiveState(r); return; }
            if (type == HostEffects.MsgNames) { if (fromHost) HostEffects.ReceiveNames(r); return; }
            if (type == RoomSync.MsgState) { if (fromHost && HostEnemies.Client) RoomSync.Receive(r); return; }
            if (type == RoomSync.MsgAsk) { if (CoopSettings.IsHost) RoomSync.HostAsked(id, r); return; }
            if (type == EnemySounds.Msg) { if (fromHost && HostEnemies.Client) EnemySounds.Receive(r); return; }
            if (type == EnemyStatus.Msg) { if (CoopSettings.IsHost) EnemyStatus.HostReceive(r); return; }
            if (type == WorldSync.MsgDoors) { if (fromHost) WorldSync.ReceiveDoors(r); return; }
            if (type == WorldSync.MsgSpin) { if (fromHost) WorldSync.ReceiveSpin(r); return; }
            if (type == WorldSync.MsgForest) { if (fromHost) WorldSync.ReceiveForest(r); return; }
            if (type == WorldSync.MsgPlate) { WorldSync.ReceivePlate(r); return; }
            if (type == MsgInfo) { if (fromHost) DungeonSync.HostDifficulty(r); return; }
            if (type == HostEnemies.MsgStunned) { if (fromHost) HostEnemies.Stunned(r); return; }
            if (type == HostEnemies.MsgStun) { if (CoopSettings.IsHost) HostEnemies.HostStun(id, r); return; }
            if (type == HordeBridge.MsgSnapshot) { if (fromHost) HordeBridge.FromHost(r); return; }
            if (type == HordeBridge.MsgToHost) { if (CoopSettings.IsHost) HordeBridge.FromClient(r); return; }
            if (type == MsgLook || type == MsgHeld)
            {
                if (!_remotes.TryGetValue(id, out var who)) return; // the state packet introduces a player
                int n = r.ReadUInt16();
                if (n > MaxLookBytes || n > data.Length - 11) return;
                var blob = r.ReadBytes(n);
                if (type == MsgHeld)
                {
                    if (who.Held != null && who.Held.AsSpan().SequenceEqual(blob)) return;
                    if (who.Held == null) Plugin.L.LogInfo($"Co-op: held items of {who.Name} received ({n} bytes).");
                    who.Held = blob; who.HeldDirty = true;
                    return;
                }
                if (who.Look != null && who.Look.AsSpan().SequenceEqual(blob)) return;
                who.Look = blob; who.LookDirty = true;
                Plugin.L.LogInfo($"Co-op: look of {who.Name} received ({n} bytes).");
                return;
            }
            if (type != MsgState) return;

            string name = r.ReadString(), scene = r.ReadString();
            var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            float yaw = r.ReadSingle();
            int seed = r.ReadInt32(), floor = r.ReadInt32();
            string bossRoom = r.ReadString();
            var body = new BodyState { MoveX = r.ReadSingle(), MoveY = r.ReadSingle(), Pitch = r.ReadSingle() };
            byte flags = r.ReadByte();
            float bow = r.ReadByte() / 255f;
            body.Grounded = (flags & 1) != 0; body.Block = (flags & 2) != 0; body.Crouch = (flags & 4) != 0;
            body.Bow = (flags & 8) != 0 ? bow : -1f; body.Down = (flags & 16) != 0; body.RunsEnemies = (flags & 32) != 0; body.FloorOpen = (flags & 64) != 0; body.Follows = (flags & 128) != 0;
            body.Action = r.ReadByte(); body.ActionSeq = r.ReadByte(); body.ActionDuration = r.ReadSingle(); body.ActionSkip = r.ReadSingle();
            byte more = r.ReadByte();
            body.Joins = (more & 1) != 0; body.Horde = (more & 2) != 0;

            if (!_remotes.TryGetValue(id, out var rem))
            {
                rem = new Remote { FirstSeen = Time.unscaledTime };
                _remotes[id] = rem;
                Plugin.L.LogInfo($"Co-op: {name} connected ({PeerName(id)}), in {scene} at {pos}.");
            }
            if (rem.Scene != scene && rem.Packets > 0) Plugin.L.LogInfo($"Co-op: {name} is now in {scene}.");
            if (rem.Body.Down != body.Down && rem.Packets > 0) Plugin.L.LogInfo(body.Down ? $"Co-op: {name} is down." : $"Co-op: {name} is back up.");
            rem.Name = name; rem.Scene = scene; rem.Pos = pos; rem.Yaw = yaw; rem.Body = body;
            rem.Seed = seed; rem.Floor = floor; rem.IsHost = fromHost;
            rem.LastSeen = Time.unscaledTime; rem.Packets++;
            if (fromHost) { DungeonSync.HostName = name; DungeonSync.HostState(scene, seed, floor, bossRoom); HostEnemies.HostRuns = body.RunsEnemies; Gather.HostOpen = body.FloorOpen; HordeBridge.HostHorde = body.Horde; }
        }
        catch (Exception e) { Warn("bad packet", e); }
    }

    static readonly Dictionary<string, int> _warned = new();
    internal static void Warn(string what, Exception e)
    {
        _warned.TryGetValue(what, out int n);
        _warned[what] = n + 1;
        if (n < 3) Plugin.L.LogWarning($"Co-op: {what} ({e.Message}).");
    }

    // ---- drawing the others ----

    // Runs after the game's animators, like the Character mod's own body update: bone touches made here
    // last until the frame is drawn.
    public static void LateTick()
    {
        if (_net == null) return;
        try { UpdateAvatars(); } catch (Exception e) { Warn("avatars", e); }
        try { HostEnemies.LateTick(); } catch (Exception e) { Warn("host enemies", e); }
        try { HostEffects.LateTick(); } catch (Exception e) { Warn("host effects", e); }
    }

    static void UpdateAvatars()
    {
        string scene = SceneManager.GetActiveScene().name;
        var cam = Camera.main;
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        foreach (var (id, rem) in _remotes.ToList())
        {
            if (now - rem.LastSeen > Timeout)
            {
                DestroyBody(rem);
                _remotes.Remove(id);
                _heard.Remove(id);
                Plugin.L.LogInfo($"Co-op: {rem.Name} disconnected.");
                continue;
            }
            bool here = rem.Scene == scene && FirstPersonController.Instance != null;
            if (!here) { Show(rem, false); continue; }

            // A partner with the Character mod sends its look within two seconds; one without never does.
            bool wantPuppet = CharacterBridge.Present && rem.Look != null && !rem.PuppetFailed;
            if (wantPuppet && rem.Puppet == null)
            {
                try
                {
                    rem.Puppet = CharacterBridge.Create("FSP_Coop_" + rem.Name, rem.Look, out var root);
                    rem.LookDirty = false; rem.HeldDirty = rem.Held != null;
                    if (rem.Avatar != null) { UnityEngine.Object.Destroy(rem.Avatar); rem.Avatar = null; rem.Anim = null; }
                    rem.Root = root.transform;
                    rem.Tag = Avatars.AddTag(root, rem.Name, 2.05f);
                    rem.Root.position = rem.Pos;
                    Plugin.L.LogInfo($"Co-op: {rem.Name} is drawn as a Character puppet.");
                }
                catch (Exception e)
                {
                    rem.PuppetFailed = true; rem.Puppet = null;
                    Plugin.L.LogWarning($"Co-op: puppet for {rem.Name} failed, using the villager avatar: {e.Message}");
                }
            }
            if (rem.Puppet != null && rem.LookDirty)
            {
                rem.LookDirty = false;
                try { CharacterBridge.ApplyLook(rem.Puppet, rem.Look); Plugin.L.LogInfo($"Co-op: {rem.Name} changed look."); }
                catch (Exception e) { Warn("look of " + rem.Name, e); }
            }
            if (rem.Puppet != null && rem.HeldDirty)
            {
                rem.HeldDirty = false;
                try { CharacterBridge.ApplyHeld(rem.Puppet, rem.Held); }
                catch (Exception e) { Warn("held items of " + rem.Name, e); }
            }
            if (rem.Puppet == null)
            {
                if (CharacterBridge.Present && rem.Look == null && !rem.PuppetFailed && now - rem.FirstSeen < 3f) continue; // its look is on the way
                // Villagers spawn a few seconds after a load: a capsule stands in until one can be cloned.
                if (rem.Avatar != null && rem.Fallback && now >= rem.NextUpgrade)
                {
                    rem.NextUpgrade = now + 2f;
                    if (Avatars.TemplateReady()) { UnityEngine.Object.Destroy(rem.Avatar); rem.Avatar = null; }
                }
                // The stand-in is made for the scene it is in (see Avatars.Create): a new scene, a new one.
                if (rem.Avatar != null && rem.AvatarScene != scene) { UnityEngine.Object.Destroy(rem.Avatar); rem.Avatar = null; }
                if (rem.Avatar == null)
                {
                    rem.AvatarScene = scene;
                    rem.Fallback = !Avatars.Create(rem.Name, out rem.Avatar, out rem.Anim, out rem.Tag);
                    rem.Root = rem.Avatar.transform;
                    rem.Root.position = rem.Pos;
                }
            }
            if (rem.Root == null) continue;
            // Standing in the same spot (both arrive at a floor's entrance): his body would wrap the camera.
            if (cam != null && !rem.Down)
            {
                Vector3 off = cam.transform.position - rem.Pos; off.y = 0f;
                if (off.sqrMagnitude < 0.3f) { Show(rem, false); continue; }
            }
            if (!rem.Root.gameObject.activeSelf) { Show(rem, true); rem.Root.position = rem.Pos; }

            var t = rem.Root;
            // A jump in position (a teleport, a new floor) is not walked through walls.
            float k = (rem.Pos - t.position).sqrMagnitude > 36f ? 1f : 1f - Mathf.Exp(-15f * dt);
            t.position = Vector3.Lerp(t.position, rem.Pos, k);
            // A player who is down lies on the floor until revived.
            var facing = rem.Down ? Quaternion.Euler(-90f, rem.Yaw, 0f) : Quaternion.Euler(0f, rem.Yaw, 0f);
            t.rotation = Quaternion.Slerp(t.rotation, facing, 1f - Mathf.Exp(-15f * dt));
            if (rem.Puppet != null)
            {
                // The partner's clock runs on while this game is paused: unscaled time.
                try { CharacterBridge.Update(rem.Puppet, rem.Body, dt); } catch (Exception e) { Warn("puppet update", e); }
            }
            else if (rem.Anim != null) Avatars.SetSpeed(rem.Anim, new Vector2(rem.Body.MoveX, rem.Body.MoveY).magnitude);
            if (rem.Tag != null && cam != null)
            {
                // The tag stays above the body and readable whichever way the body lies.
                rem.Tag.position = t.position + Vector3.up * (rem.Down ? 0.9f : rem.Puppet != null ? 2.05f : 2.2f);
                rem.Tag.rotation = Quaternion.LookRotation(rem.Tag.position - cam.transform.position);
            }
        }
    }

    static void Show(Remote rem, bool on)
    {
        if (rem.Root != null && rem.Root.gameObject.activeSelf != on) rem.Root.gameObject.SetActive(on);
    }

    static void DestroyBody(Remote rem)
    {
        if (rem.Puppet != null) try { CharacterBridge.Destroy(rem.Puppet); } catch { }
        if (rem.Avatar != null) UnityEngine.Object.Destroy(rem.Avatar);
        rem.Puppet = null; rem.Avatar = null; rem.Root = null;
    }
}

internal static class Avatars
{
    static GameObject _template;

    // A villager NPC is cloned once (town has them) and stripped down to its meshes and animator;
    // without one yet, a plain capsule stands in.
    public static bool TemplateReady() { if (_template == null) _template = MakeTemplate(); return _template != null; }

    // Returns false when only the capsule stand-in could be made.
    public static bool Create(string name, out GameObject avatar, out Animator anim, out Transform tag)
    {
        if (_template == null) _template = MakeTemplate();
        if (_template != null)
        {
            avatar = UnityEngine.Object.Instantiate(_template);
            avatar.SetActive(true);
            // Away from the village the villager's own materials draw as error pink (their shader setup
            // belongs to that scene): a plain lit material keeps the figure readable.
            var plain = SceneManager.GetActiveScene().name != "MAIN" ? Plain() : null;
            if (plain != null)
                foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(Mathf.Max(1, r.sharedMaterials.Length));
                    for (int i = 0; i < mats.Length; i++) mats[i] = plain;
                    r.sharedMaterials = mats;
                }
        }
        else
        {
            // The state packet carries the feet position; a capsule's origin is its middle.
            avatar = new GameObject();
            var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            UnityEngine.Object.Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(avatar.transform, false);
            cap.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            cap.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            // Unity's default material has no shader in this render pipeline: make a plain lit one,
            // or borrow a scene material when that shader is not in the build.
            var plain = Plain();
            var any = UnityEngine.Object.FindObjectOfType<MeshRenderer>();
            if (plain != null) cap.GetComponent<MeshRenderer>().sharedMaterial = plain;
            else if (any != null) cap.GetComponent<MeshRenderer>().sharedMaterial = any.sharedMaterial;
        }
        avatar.name = "FSP_Coop_" + name;
        UnityEngine.Object.DontDestroyOnLoad(avatar);
        anim = avatar.GetComponentInChildren<Animator>();
        tag = AddTag(avatar, name, 2.2f);
        return _template != null;
    }

    static Material _plain;
    static Material Plain()
    {
        if (_plain != null) return _plain;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return null;
        _plain = new Material(shader) { color = new Color(0.85f, 0.7f, 0.4f), hideFlags = HideFlags.DontUnloadUnusedAsset };
        return _plain;
    }

    public static Transform AddTag(GameObject body, string name, float height)
    {
        var tagGo = new GameObject("Tag");
        tagGo.transform.SetParent(body.transform, false);
        tagGo.transform.localPosition = new Vector3(0, height, 0);
        var tmp = tagGo.AddComponent<TextMeshPro>();
        tmp.text = name; tmp.fontSize = 3f; tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.85f, 0.45f);
        var font = FirstPersonController.Instance?.interactionText?.font;
        if (font != null) tmp.font = font;
        return tagGo.transform;
    }

    static GameObject MakeTemplate()
    {
        var npc = UnityEngine.Object.FindObjectOfType<NPC_Controller>();
        if (npc == null) return null;
        var copy = UnityEngine.Object.Instantiate(npc.gameObject);
        copy.SetActive(false);
        copy.name = "FSP_CoopTemplate";
        UnityEngine.Object.DontDestroyOnLoad(copy);
        // Remove what makes it a villager (AI, navigation, physics, sounds, interaction). Visual helper
        // components stay: stripping everything left the body with a broken (magenta) material.
        foreach (var c in copy.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            var type = c.GetIl2CppType().Name;
            if (type is "NPC_Controller" or "NavMeshAgent" or "Rigidbody" or "AudioSource" or "FootstepSwapper"
                || type.EndsWith("Collider"))
                UnityEngine.Object.DestroyImmediate(c);
        }
        copy.transform.position = Vector3.zero;
        return copy;
    }

    public static void SetSpeed(Animator a, float speed)
    {
        foreach (var p in a.parameters)
            if (p.type == AnimatorControllerParameterType.Float && p.name.Contains("Speed")) { a.SetFloat(p.name, Mathf.Clamp(speed / 4f, 0f, 1.5f)); return; }
    }
}
