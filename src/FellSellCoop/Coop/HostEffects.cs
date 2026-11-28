using System;
using System.Collections.Generic;
using System.IO;
using Il2CppEmeraldAI.Utility;
using UnityEngine;

namespace FellSellPlus.Coop;

// Enemy projectiles, spells and hit effects on a client (part of HostEnemies).
//
// Everything Emerald AI throws or flashes (arrows, fireballs, ground spells, cast, muzzle and impact
// effects, with their sounds) is taken from its object pool, so on the host each of them is an active
// pooled object. The host lists those twenty times a second (which prefab, where, which way it faces);
// a client keeps a look-alike of each at the reported place: the same prefab with its scripts,
// colliders and physics off, so it flies and sounds but hits nothing. The hit itself reaches the
// client as damage through its stand-in on the host (HostEnemies).
internal static class HostEffects
{
    public const byte MsgState = 8, MsgNames = 9;
    const string SoundName = "Emerald Sound";
    const float SendInterval = 0.05f, NamesInterval = 1f, CopyTimeout = 0.35f;
    const int PerPacket = 36;   // 30 bytes each: one datagram
    const int HeaderCount = 13; // type, origin, world key, then the count byte

    // ---------------------------------------------------------------- host

    sealed class Live { public GameObject Go; public byte Name, Gen; public bool Seen; }

    static readonly Dictionary<int, Live> _live = new();
    static readonly Dictionary<int, byte> _gens = new();
    static readonly Dictionary<string, byte> _nameIds = new();
    static readonly List<string> _names = new();
    static readonly List<int> _drop = new();
    static float _nextSend, _nextNames;
    static bool _namesDirty;

    internal static int LiveCount => _live.Count;
    internal static int CopyCount => _copies.Count;

    public static void HostPackets(List<byte[]> into, int key)
    {
        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + SendInterval;
        if (key == 0) { _live.Clear(); return; }

        foreach (var l in _live.Values) l.Seen = false;
        foreach (var member in UnityEngine.Object.FindObjectsOfType<EmeraldObjectPool.PoolMember>())
        {
            var go = member.gameObject;
            int id = go.GetInstanceID();
            if (_live.TryGetValue(id, out var live)) { live.Seen = true; continue; }
            // Floating damage numbers and other interface pieces are each player's own.
            if (go.GetComponent<RectTransform>() != null) continue;
            string name = go.name;
            int cut = name.IndexOf('(');
            if (cut > 0) name = name.Substring(0, cut).TrimEnd();
            if (name == SoundName)
            {
                // A sound player made at run time, not a prefab: it is known by the clip it plays.
                var src = go.GetComponentInChildren<AudioSource>(true);
                var clip = src != null ? src.clip : null;
                if (clip == null) continue; // not started yet, the next pass sees it
                name = SoundName + "|" + clip.name;
            }
            if (!_nameIds.TryGetValue(name, out byte nameId))
            {
                if (_names.Count >= 255) continue;
                nameId = (byte)_names.Count;
                _names.Add(name); _nameIds[name] = nameId; _namesDirty = true;
                if (_names.Count <= 60) Plugin.L.LogInfo($"Co-op effects: '{name}' is shown to the clients from now on.");
            }
            // A pooled object comes back under the same id: the generation tells a client it is a new one.
            _gens.TryGetValue(id, out byte gen);
            _gens[id] = ++gen;
            _live[id] = new Live { Go = go, Name = nameId, Gen = gen, Seen = true };
        }
        _drop.Clear();
        foreach (var kv in _live) if (!kv.Value.Seen || kv.Value.Go == null) _drop.Add(kv.Key);
        foreach (int id in _drop) _live.Remove(id);

        if (_names.Count > 0 && (_namesDirty || Time.unscaledTime >= _nextNames))
        {
            _namesDirty = false; _nextNames = Time.unscaledTime + NamesInterval;
            MemoryStream nms = null; BinaryWriter nw = null; int nn = 0;
            for (int i = 0; i < _names.Count; i++)
            {
                if (nms == null) { nw = CoopModule.Begin(MsgNames, out nms); nw.Write((byte)0); }
                nw.Write((byte)i); nw.Write(_names[i]);
                if (++nn >= 24 || i == _names.Count - 1)
                {
                    var data = nms.ToArray(); data[9] = (byte)nn; into.Add(data); nms = null; nn = 0;
                }
            }
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
        foreach (var kv in _live)
        {
            var l = kv.Value;
            if (ms == null) { w = CoopModule.Begin(MsgState, out ms); w.Write(key); w.Write((byte)0); }
            var t = l.Go.transform;
            Vector3 p = t.position, e = t.eulerAngles;
            w.Write(kv.Key); w.Write(l.Gen); w.Write(l.Name);
            w.Write(p.x); w.Write(p.y); w.Write(p.z); w.Write(e.x); w.Write(e.y); w.Write(e.z);
            if (++n >= PerPacket) Flush();
        }
        Flush();
    }

    // ---------------------------------------------------------------- client

    sealed class Copy { public GameObject Go; public byte Gen; public float Seen; public Vector3 Pos; public Quaternion Rot; }

    static readonly Dictionary<int, Copy> _copies = new();
    static readonly Dictionary<byte, string> _hostNames = new();
    static readonly Dictionary<string, GameObject> _prefabs = new();
    static readonly HashSet<string> _missing = new();
    static readonly List<int> _goneCopies = new();
    static float _nextIndex;
    static int _shown;

    public static void ReceiveNames(BinaryReader r)
    {
        int count = r.ReadByte();
        for (int i = 0; i < count; i++) { byte id = r.ReadByte(); _hostNames[id] = r.ReadString(); }
    }

    public static void ReceiveState(BinaryReader r)
    {
        int key = r.ReadInt32();
        int count = r.ReadByte();
        if (!World.ClientShares || World.ClientKey != key) return;
        float now = Time.unscaledTime;
        for (int i = 0; i < count; i++)
        {
            int id = r.ReadInt32(); byte gen = r.ReadByte(), nameId = r.ReadByte();
            var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var rot = Quaternion.Euler(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (_copies.TryGetValue(id, out var c) && (c.Gen != gen || c.Go == null))
            {
                if (c.Go != null) UnityEngine.Object.Destroy(c.Go);
                _copies.Remove(id); c = null;
            }
            if (c == null)
            {
                if (!_hostNames.TryGetValue(nameId, out string name)) continue; // its name is on the way
                var go = Make(name, pos, rot);
                if (go == null) continue;
                c = new Copy { Go = go, Gen = gen };
                _copies[id] = c;
                if (_shown++ < 20) Plugin.L.LogInfo($"Co-op effects: showing the host's '{name}'.");
            }
            c.Pos = pos; c.Rot = rot; c.Seen = now;
        }
    }

    public static void LateTick()
    {
        if (_copies.Count == 0) return;
        bool here = World.ClientShares;
        float now = Time.unscaledTime, k = 1f - Mathf.Exp(-30f * Time.unscaledDeltaTime);
        _goneCopies.Clear();
        foreach (var kv in _copies)
        {
            var c = kv.Value;
            if (!here || c.Go == null || now - c.Seen > CopyTimeout)
            {
                if (c.Go != null) UnityEngine.Object.Destroy(c.Go);
                _goneCopies.Add(kv.Key);
                continue;
            }
            var t = c.Go.transform;
            t.position = (c.Pos - t.position).sqrMagnitude > 16f ? c.Pos : Vector3.Lerp(t.position, c.Pos, k);
            t.rotation = c.Rot;
        }
        foreach (int id in _goneCopies) _copies.Remove(id);
    }

    static readonly Dictionary<string, AudioClip> _clips = new();
    static float _nextClipIndex;
    static int _sounds;
    static AudioSource _voice; // an enemy's own audio source: how loud and how far enemy sounds carry here

    // The host's sound player: the same clip, played here at the same place.
    static GameObject MakeSound(string name, Vector3 pos)
    {
        string clipName = name.Substring(SoundName.Length + 1);
        if (!_clips.TryGetValue(clipName, out var clip) || clip == null)
        {
            if (Time.unscaledTime < _nextClipIndex) return null;
            _nextClipIndex = Time.unscaledTime + 5f;
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>()) _clips[c.name] = c;
            if (!_clips.TryGetValue(clipName, out clip) || clip == null) { _missing.Add(name); return null; }
        }
        if (_sounds++ < 10) Plugin.L.LogInfo($"Co-op effects: playing the host's sound '{clipName}'.");
        var go = new GameObject("FSP_CoopSound_" + clipName);
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        if (_voice == null)
        {
            var sounds = UnityEngine.Object.FindObjectOfType<Il2CppEmeraldAI.EmeraldSounds>(true);
            if (sounds != null) _voice = sounds.m_AudioSource != null ? sounds.m_AudioSource : sounds.GetComponent<AudioSource>();
        }
        if (_voice != null)
        {
            src.outputAudioMixerGroup = _voice.outputAudioMixerGroup;
            src.rolloffMode = _voice.rolloffMode; src.minDistance = _voice.minDistance; src.maxDistance = _voice.maxDistance;
        }
        else { src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 2f; src.maxDistance = 35f; }
        src.spatialBlend = 1f;
        src.clip = clip;
        src.Play();
        return go;
    }

    static GameObject Make(string name, Vector3 pos, Quaternion rot)
    {
        if (_missing.Contains(name)) return null;
        if (name.StartsWith(SoundName + "|")) return MakeSound(name, pos);
        if (!_prefabs.TryGetValue(name, out var prefab) || prefab == null)
        {
            // One pass over the loaded assets finds every effect prefab by name; a miss waits for the next pass.
            if (Time.unscaledTime < _nextIndex) return null;
            _nextIndex = Time.unscaledTime + 5f;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene.IsValid() || go.transform.parent != null) continue;
                string n = go.name;
                if (!_prefabs.ContainsKey(n)) _prefabs[n] = go;
            }
            if (!_prefabs.TryGetValue(name, out prefab) || prefab == null)
            {
                _missing.Add(name);
                Plugin.L.LogWarning($"Co-op effects: no prefab named '{name}' is loaded here, it is not shown.");
                return null;
            }
        }
        try
        {
            var copy = UnityEngine.Object.Instantiate(prefab, pos, rot);
            copy.name = "FSP_CoopEffect_" + name;
            foreach (var c in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (c != null && !Harmless(c)) c.enabled = false;
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; }
            copy.SetActive(true);
            return copy;
        }
        catch (Exception e) { _missing.Add(name); Plugin.L.LogWarning($"Co-op effects: copy of '{name}' failed: {e.Message}"); return null; }
    }

    // Scripts that only animate how an effect looks or sounds stay on; everything else (flight, damage,
    // despawn timers) belongs to the host's object.
    static bool Harmless(MonoBehaviour c)
    {
        var type = c.GetIl2CppType();
        string ns = type.Namespace ?? "", name = type.Name;
        return ns == "EpicToonFX" || name is "LightFlicker" or "LightMotion" or "LightSway" or "TrailRendererHelper";
    }
}
