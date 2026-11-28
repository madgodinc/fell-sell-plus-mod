using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FellSellCharacter;

public enum PuppetAction { None, AttackR1, AttackR2, AttackL1, AttackL2, PunchR, PunchL, CastR, CastL, Drink }

// Everything the animation needs to know about its owner each frame. The local player fills it from
// the game; a co-op peer would fill it from the network.
public struct PuppetState
{
    public Vector2 Move;      // velocity in the body's own frame, m/s: x right, y forward
    public bool Grounded;
    public float Pitch;       // look pitch in degrees, positive down
    public bool Block;
    public float Bow;         // negative: no bow; 0..1: how far the string is drawn
    public bool Crouch;
}

// Full-body animation for a Puppet from the game's own humanoid clips, mixed by a playable graph:
// a locomotion blend (idle, walk and run in four directions) under an upper-body action layer.
// There is no controller asset to build at runtime, and the graph needs none.
public sealed class PuppetAnimator
{
    const int Idle = 0, WalkF = 1, WalkL = 2, WalkR = 3, RunF = 4, RunB = 5, RunL = 6, RunR = 7, LocoCount = 8;
    const int ActBlock = 0, ActBow = 1, ActFirstShot = 2, ActCount = ActFirstShot + (int)PuppetAction.Drink;
    const float WalkSpeed = 1.6f, RunSpeed = 4.2f; // ground speed the clips were made for, m/s

    static readonly string[][] LocoClips =
    {
        new[] { "A_MOD_IDL_Base_Masc", "Unarmed-Idle", "Standing Idle", "2Hand-Sword-Idle" },
        new[] { "Standing Walk Forward", "Standing Walk Forward(1)", "2Hand-Sword-Walk" },
        new[] { "Standing Walk Left", "Standing Walk Left(1)", "Unarmed-Strafe-Left" },
        new[] { "Standing Walk Right", "Standing Walk Right(1)", "Unarmed-Strafe-Right" },
        new[] { "Standing Run Forward", "Standing Run Forward(1)", "2Hand-Sword-Run-Forward" },
        new[] { "Standing Run Back" },
        new[] { "Standing Run Left", "Standing Run Left(1)", "2Hand-Sword-Strafe-Left" },
        new[] { "Standing Run Right", "Standing Run Right(1)", "2Hand-Sword-Strafe-Right" },
    };

    static readonly Dictionary<PuppetAction, string[]> ActionClips = new()
    {
        { PuppetAction.AttackR1, new[] { "sword and shield slash", "2Hand-Sword-Attack2" } },
        { PuppetAction.AttackR2, new[] { "sword and shield attack (2)", "2Hand-Sword-Attack3" } },
        { PuppetAction.AttackL1, new[] { "Unarmed-Attack-L3", "2Hand-Sword-Attack3", "sword and shield slash" } },
        { PuppetAction.AttackL2, new[] { "Unarmed-Attack-L2", "2Hand-Sword-Attack2", "sword and shield attack (2)" } },
        { PuppetAction.PunchR, new[] { "Unarmed-Attack-R1", "sword and shield slash" } },
        { PuppetAction.PunchL, new[] { "Unarmed-Attack-L1", "sword and shield slash" } },
        { PuppetAction.CastR, new[] { "Standing 1H Magic Attack 01" } },
        { PuppetAction.CastL, new[] { "Standing 1H Magic Attack 01 Mirror", "Standing 1H Magic Attack 01" } },
        { PuppetAction.Drink, new[] { "Drinking", "Standing 1H Magic Attack 01" } },
    };
    static readonly string[] BlockClips = { "sword and shield block idle", "Male Sword Block" };
    static readonly string[] BowClips = { "Standing Draw Arrow(1)" };

    // Clips are not addressable: they are in memory only while something loaded uses them. Whatever was
    // found once is pinned, so a later scene without that enemy still has it.
    static readonly Dictionary<string, AnimationClip> Pinned = new();
    static float _lastScan = -100f;

    static AnimationClip Find(string[] names)
    {
        foreach (var n in names)
            if (Pinned.TryGetValue(n, out var c) && c != null) return c;
        if (Time.unscaledTime - _lastScan > 5f)
        {
            _lastScan = Time.unscaledTime;
            foreach (var c in Resources.FindObjectsOfTypeAll<AnimationClip>())
            {
                if (c == null) continue;
                string n = c.name;
                if (Pinned.TryGetValue(n, out var old) && old != null) continue;
                if (!Wanted.Contains(n)) continue;
                c.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                Pinned[n] = c;
            }
            foreach (var n in names)
                if (Pinned.TryGetValue(n, out var c) && c != null) return c;
        }
        return null;
    }

    static HashSet<string> _wanted;
    static HashSet<string> Wanted
    {
        get
        {
            if (_wanted != null) return _wanted;
            _wanted = new HashSet<string>();
            foreach (var a in LocoClips) foreach (var n in a) _wanted.Add(n);
            foreach (var a in ActionClips.Values) foreach (var n in a) _wanted.Add(n);
            foreach (var n in BlockClips) _wanted.Add(n);
            foreach (var n in BowClips) _wanted.Add(n);
            return _wanted;
        }
    }

    // Call when a new scene has loaded: clips that were missing get another chance.
    public static void Rescan() => _lastScan = -100f;

    readonly Puppet _puppet;
    PlayableGraph _graph;
    PlayableHandle _loco, _act, _layers;
    readonly PlayableHandle[] _locoClip = new PlayableHandle[LocoCount];
    readonly float[] _locoLen = new float[LocoCount];
    readonly bool[] _locoHas = new bool[LocoCount];
    readonly float[] _locoW = new float[LocoCount];
    readonly PlayableHandle[] _actClip = new PlayableHandle[ActCount];
    readonly bool[] _actHas = new bool[ActCount];
    readonly float[] _actLen = new float[ActCount];
    readonly float[] _actW = new float[ActCount];
    bool _built;
    int _missing;
    float _nextBuild;

    float _phase, _idleTime, _moveBlend, _runBlend, _layerW;
    Vector2 _dir = new(0f, 1f);
    PuppetAction _action;
    float _actionTime, _actionSpeed = 1f, _bowHold;
    public bool Ready => _built;

    public PuppetAnimator(Puppet puppet) { _puppet = puppet; }

    bool Build()
    {
        Dispose();
        var idle = Find(LocoClips[Idle]);
        if (idle == null) return false;
        _graph = PlayableGraph.Create(_puppet.Root.name + "_Graph");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _loco = AnimationMixerPlayable.Create(_graph, LocoCount).m_Handle;
        _act = AnimationMixerPlayable.Create(_graph, ActCount).m_Handle;
        var layers = AnimationLayerMixerPlayable.Create(_graph, 2, false);
        _layers = layers.m_Handle;

        _missing = 0;
        for (int i = 0; i < LocoCount; i++)
        {
            var clip = Find(LocoClips[i]);
            _locoHas[i] = clip != null;
            if (clip == null) { _missing++; continue; }
            _locoLen[i] = Mathf.Max(0.1f, clip.length);
            _locoClip[i] = Clip(clip, _loco, i);
        }
        for (int i = 0; i < ActCount; i++)
        {
            var clip = Find(ActionNames(i));
            _actHas[i] = clip != null;
            if (clip == null) { _missing++; continue; }
            _actLen[i] = Mathf.Max(0.1f, clip.length);
            _actClip[i] = Clip(clip, _act, i);
        }

        _graph.ConnectInternal(_loco, 0, _layers, 0);
        _graph.ConnectInternal(_act, 0, _layers, 1);
        _layers.SetInputWeight(0, 1f);
        _layers.SetInputWeight(1, 0f);
        try
        {
            // Actions move the body above the hips; the legs keep walking.
            var mask = new AvatarMask();
            foreach (var part in new[] { AvatarMaskBodyPart.Root, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg, AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK })
                mask.SetHumanoidBodyPartActive(part, false);
            layers.SetLayerMaskFromAvatarMask(1, mask);
        }
        catch (Exception e) { Plugin.L.Warning("Upper-body mask is unavailable, actions play on the whole body: " + e.Message); }

        var output = AnimationPlayableOutput.Create(_graph, "Body", _puppet.Animator);
        output.m_Handle.SetSourcePlayable(_layers, 0);
        _graph.Play();
        _built = true;
        return true;
    }

    PlayableHandle Clip(AnimationClip clip, PlayableHandle mixer, int port)
    {
        var p = AnimationClipPlayable.Create(_graph, clip);
        p.SetApplyFootIK(false);
        var h = p.m_Handle;
        h.SetSpeed(0); // time is set by hand every frame, so all strides stay in step
        _graph.ConnectInternal(h, 0, mixer, port);
        mixer.SetInputWeight(port, 0f);
        return h;
    }

    static string[] ActionNames(int input) =>
        input == ActBlock ? BlockClips : input == ActBow ? BowClips : ActionClips[(PuppetAction)(input - ActFirstShot + 1)];

    static int Input(PuppetAction a) => ActFirstShot + (int)a - 1;

    // duration: how long the action should take in seconds (0 = the clip's own length);
    // skip: the share of the clip to leave out at its start.
    public void Play(PuppetAction action, float duration = 0f, float skip = 0f)
    {
        if (!_built || action == PuppetAction.None || !_actHas[Input(action)]) return;
        float len = _actLen[Input(action)];
        _action = action;
        _actionTime = len * Mathf.Clamp01(skip);
        _actionSpeed = duration > 0.05f ? Mathf.Clamp((len - _actionTime) / duration, 0.5f, 3f) : 1f;
    }

    public void Update(in PuppetState s, float dt)
    {
        if (_puppet == null || !_puppet.Alive) return;
        if (!_built || (_missing > 0 && Time.unscaledTime > _nextBuild))
        {
            // Rebuild while clips are missing: the enemy that carries them may have loaded since.
            bool had = _built; int before = _missing;
            if (!had || HasNewClips())
            {
                if (!Build() && !had) { _nextBuild = Time.unscaledTime + 2f; return; }
                if (_missing != before || !had) Plugin.L.Msg($"Body animation ready ({_missing} clip(s) missing).");
            }
            _nextBuild = Time.unscaledTime + 5f;
        }
        if (!_built) return;
        dt = Mathf.Clamp(dt, 0f, 0.1f);

        // Locomotion.
        float speed = s.Move.magnitude;
        bool moving = speed > 0.25f;
        if (moving) _dir = Vector2.Lerp(_dir, s.Move / speed, 1f - Mathf.Exp(-12f * dt)).normalized;
        _moveBlend = Mathf.MoveTowards(_moveBlend, moving ? 1f : 0f, dt * 6f);
        _runBlend = Mathf.MoveTowards(_runBlend, Mathf.InverseLerp(1.8f, 2.6f, speed), dt * 5f);
        Array.Clear(_locoW, 0, LocoCount);
        float f = Mathf.Max(0f, _dir.y), b = Mathf.Max(0f, -_dir.y), l = Mathf.Max(0f, -_dir.x), r = Mathf.Max(0f, _dir.x);
        float sum = f + b + l + r;
        if (sum < 1e-4f) { f = 1f; sum = 1f; }
        f /= sum; b /= sum; l /= sum; r /= sum;
        float walk = _moveBlend * (1f - _runBlend), run = _moveBlend * _runBlend;
        Add(WalkF, walk * f); Add(WalkL, walk * l); Add(WalkR, walk * r);
        Add(RunB, walk * b);                                  // no walk-back clip for people: the run-back one, slowed
        Add(RunF, run * f); Add(RunB, run * b); Add(RunL, run * l); Add(RunR, run * r);
        float used = 0f, cycle = 0f;
        for (int i = 1; i < LocoCount; i++) { used += _locoW[i]; cycle += _locoW[i] * _locoLen[i]; }
        _locoW[Idle] = Mathf.Max(0f, 1f - used);
        if (used > 1e-3f)
        {
            cycle /= used;
            float natural = Mathf.Lerp(WalkSpeed, RunSpeed, _runBlend);
            float rate = Mathf.Clamp(speed / natural, 0.6f, 1.7f);
            if (!s.Grounded) rate *= 0.35f; // no jump clip in the game: the stride just slows in the air
            _phase = Mathf.Repeat(_phase + dt * rate / Mathf.Max(0.2f, cycle), 1f);
        }
        _idleTime += dt;
        for (int i = 0; i < LocoCount; i++)
        {
            if (!_locoHas[i]) continue;
            _loco.SetInputWeight(i, _locoW[i]);
            if (_locoW[i] <= 0f && i != Idle) continue;
            _locoClip[i].SetTime(i == Idle ? Mathf.Repeat(_idleTime, _locoLen[i]) : _phase * _locoLen[i]);
        }

        // Upper-body layer: a one-shot, else the bow draw, else the block.
        int want = -1;
        if (_action != PuppetAction.None)
        {
            int shot = Input(_action);
            _actionTime += dt * _actionSpeed;
            if (_actionTime >= _actLen[shot] * 0.92f) _action = PuppetAction.None;
            else { want = shot; _actClip[shot].SetTime(_actionTime); }
        }
        if (s.Bow >= 0f) _bowHold = 0.25f; else _bowHold -= dt;
        if (want < 0 && (s.Bow >= 0f || _bowHold > 0f) && _actHas[ActBow]) want = ActBow;
        if (want < 0 && s.Block && _actHas[ActBlock]) want = ActBlock;
        if (_actHas[ActBlock]) _actClip[ActBlock].SetTime(Mathf.Repeat(_idleTime, _actLen[ActBlock]));
        if (_actHas[ActBow] && s.Bow >= 0f) _actClip[ActBow].SetTime(Mathf.Clamp01(s.Bow) * (_actLen[ActBow] - 0.02f));
        float actSum = 0f;
        for (int i = 0; i < ActCount; i++)
        {
            if (!_actHas[i]) continue;
            _actW[i] = Mathf.MoveTowards(_actW[i], i == want ? 1f : 0f, dt * 10f);
            actSum += _actW[i];
        }
        // A mixer whose inputs sum below one blends toward the bind pose: normalise, and fade the layer instead.
        for (int i = 0; i < ActCount; i++)
            if (_actHas[i]) _act.SetInputWeight(i, actSum > 1e-3f ? _actW[i] / actSum : 0f);
        _layerW = Mathf.MoveTowards(_layerW, want >= 0 ? 1f : 0f, dt * 9f);
        _layers.SetInputWeight(1, actSum > 1e-3f ? _layerW : 0f);

        _graph.Evaluate(0f);
    }

    void Add(int i, float w) { if (_locoHas[i]) _locoW[i] += w; }

    bool HasNewClips()
    {
        for (int i = 0; i < LocoCount; i++) if (!_locoHas[i] && Find(LocoClips[i]) != null) return true;
        for (int i = 0; i < ActCount; i++) if (!_actHas[i] && Find(ActionNames(i)) != null) return true;
        return false;
    }

    public bool Busy => _action != PuppetAction.None;

    public void Dispose()
    {
        if (_built && _graph.IsValid()) _graph.Destroy();
        _built = false;
        for (int i = 0; i < ActCount; i++) { _actHas[i] = false; _actW[i] = 0f; }
        for (int i = 0; i < LocoCount; i++) _locoHas[i] = false;
        _action = PuppetAction.None;
    }
}
