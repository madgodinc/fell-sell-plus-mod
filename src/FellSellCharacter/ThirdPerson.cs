using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FellSellCharacter;

// The local player's third-person view.
//
// The game stays first person in every respect: its camera, rays, hands and weapons are untouched.
// Only while a frame is being rendered the camera sits behind the right shoulder, turned so that the
// screen centre shows the very point the first-person view ray hits. So the crosshair, attacks,
// projectiles and interaction all agree without changing any game logic. The first-person arms are
// hidden for that render and a full-body puppet stands where the hero is.
internal static class ThirdPerson
{
    internal static Puppet Body;
    internal static PuppetAnimator Anim;
    internal static Look Look;
    internal static bool Preview;          // the character editor is open: orbit camera, body shown
    internal static bool LateHookAlive, RenderHookAlive;

    static bool Active => Settings.ThirdPerson.Value;
    static IntPtr _fpcPtr;
    static PlayerCombat _combat;
    static Transform _fpRoot;              // PlayerVisualised: arms, held items and slash effects of the first-person view
    static bool _fpHidden;
    static float _nextFpScan;
    static bool _shown, _armsDone;
    static float _nextBodyTry;

    internal static void OnSceneChanged()
    {
        PuppetAnimator.Rescan();
        _fpcPtr = IntPtr.Zero; _combat = null; _fpRoot = null; _fpHidden = false;
        _right.Reset(); _left.Reset();
    }

    internal static void Tick()
    {
        var fpc = FirstPersonController.Instance;
        if (fpc == null) return;
        var kb = Keyboard.current;
        if (kb == null || Preview) return;
        if (Enum.TryParse<Key>(Settings.ViewKey.Value, true, out var key) && key != Key.None && kb[key].wasPressedThisFrame && InControl(fpc))
        {
            Settings.ThirdPerson.Value = !Settings.ThirdPerson.Value;
            MelonLoader.MelonPreferences.Save();
        }
    }

    // The player is steering the hero: no menu, no pause.
    internal static bool InControl(FirstPersonController fpc) =>
        Time.timeScale > 0f && Cursor.lockState == CursorLockMode.Locked && fpc != null && !fpc._isDead;

    internal static bool Bind(FirstPersonController fpc)
    {
        if (fpc.Pointer == _fpcPtr && _combat != null && _fpRoot != null) return true;
        _fpcPtr = fpc.Pointer;
        _combat = fpc._playerCombat != null ? fpc._playerCombat : fpc.GetComponentInChildren<PlayerCombat>(true);
        var hero = _combat != null ? _combat.attackAnimator : null;
        _fpRoot = hero != null ? hero.transform.parent : null;
        _fpHidden = false; _armsDone = false;
        _right.Reset(); _left.Reset();
        _fingers.Clear();
        return _combat != null && _fpRoot != null;
    }

    internal static bool EnsureReady(FirstPersonController fpc) => fpc != null && Bind(fpc) && EnsureBody();

    internal static bool EnsureBody()
    {
        if (Body != null && Body.Alive) return true;
        if (Time.unscaledTime < _nextBodyTry) return false;
        try
        {
            Look ??= Look.LoadSaved();
            Body = Puppet.Create("FSC_Hero");
            Body.Apply(Look);
            Anim = new PuppetAnimator(Body);
            _right.Reset(); _left.Reset();
            _fingers.Clear();
            Plugin.L.Msg("Hero body built.");
            return true;
        }
        catch (Exception e)
        {
            Plugin.L.Error("Hero body failed: " + e);
            Body?.Destroy(); Body = null; Anim = null;
            _nextBodyTry = Time.unscaledTime + 10f;
            return false;
        }
    }

    internal static void ApplyLook(Look look)
    {
        Look = look;
        if (Body != null && Body.Alive) Body.Apply(look);
        _armsDone = false;
    }

    // Runs after the game's hand animator each frame (it shows, hides and poses the first-person arms).
    internal static void LateTick()
    {
        var fpc = FirstPersonController.Instance;
        bool show = RenderHookAlive && fpc != null && Plugin.InGame && Bind(fpc) && (Preview || (Active && !fpc._isDead && !FirstPersonController.IsCameraAwayFromPlayer()));
        if (show && !EnsureBody()) show = false;
        if (!_armsDone && fpc != null && Plugin.InGame && Bind(fpc))
        {
            // The first-person arms wear the look as well; a custom one needs the body built for its meshes.
            _armsDone = true;
            Look ??= Look.LoadSaved();
            if (FirstPersonArms.IsDefault(Look) || EnsureBody())
                try { FirstPersonArms.Apply(_fpRoot, Look, Body); } catch (Exception e) { Plugin.Fail("First-person arms", e); }
        }
        _shown = show;
        if (Body != null && Body.Alive) Body.SetVisible(show);
        HideFirstPerson(show);
        PreviewLight(show && Preview);
        if (!show) return;

        var cc = fpc.characterController;
        var pt = fpc.transform;
        float scale = pt.lossyScale.y;
        var root = Body.Root.transform;
        Vector3 feet = pt.position + Vector3.up * ((cc.center.y - cc.height * 0.5f) * scale);
        root.SetPositionAndRotation(feet, Quaternion.Euler(0f, pt.eulerAngles.y, 0f));
        root.localScale = Vector3.one * scale;

        var state = new PuppetState { Grounded = true, Bow = -1f };
        float dt = Time.unscaledDeltaTime;
        if (!Preview)
        {
            dt = Time.deltaTime;
            Sample(fpc, out state);
        }
        Anim.Update(state, dt);
        if (!Anim.Ready) return;
        _poser.Apply(Body, state, dt);
        var eq = PlayerEquipment.Instance;
        if (eq != null)
        {
            _right.Sync(eq.currentRightHandItem, eq.rightHandHolder, Body.Bone("hand_r"), true);
            _left.Sync(eq.currentLeftHandItem, eq.leftHandHolder, Body.Bone("hand_l"), false);
            CopyFingers(eq.rightHandHolder, _right.Has, "_r");
            CopyFingers(eq.leftHandHolder, _left.Has, "_l");
        }
    }

    // ---- what the first-person hands are doing ----

    // The hero's state is read once a frame, whoever asks first: the body update here or the co-op mod.
    static int _sampleFrame = -1;
    static PuppetState _sample;
    internal static int ActionSeq;
    internal static PuppetAction LastAction;
    internal static float LastDuration, LastSkip;

    internal static bool Sample(FirstPersonController fpc, out PuppetState state)
    {
        state = new PuppetState { Grounded = true, Bow = -1f };
        if (fpc == null || !Bind(fpc)) return false;
        if (Time.frameCount == _sampleFrame) { state = _sample; return true; }
        var cc = fpc.characterController;
        Vector3 v = Quaternion.Inverse(Quaternion.Euler(0f, fpc.transform.eulerAngles.y, 0f)) * cc.velocity;
        state.Move = new Vector2(v.x, v.z);
        state.Grounded = fpc.IsGrounded;
        state.Pitch = fpc.verticalRotation;
        state.Crouch = fpc.IsCrouching;
        ReadCombat(fpc, ref state);
        _sample = state; _sampleFrame = Time.frameCount;
        return true;
    }

    static Dictionary<int, PuppetAction> _stateActions;
    static int _hashR, _hashL;
    static float _timeR, _timeL, _bowDraw = -1f;

    static void ReadCombat(FirstPersonController fpc, ref PuppetState state)
    {
        if (_combat == null) return;
        state.Block = _combat.IsBlocking;
        var hero = _combat.attackAnimator;
        if (hero != null)
        {
            _stateActions ??= BuildStateActions();
            Watch(hero, _combat.rightLayerIndex, ref _hashR, ref _timeR);
            Watch(hero, _combat.leftLayerIndex, ref _hashL, ref _timeL);
        }
        // The Bows mod: the bow is a left-hand item drawn while the attack button is held.
        var eq = PlayerEquipment.Instance;
        var left = eq != null ? eq.currentLeftHandItem : null;
        bool bow = left != null && left.name.StartsWith("FSB_Bow");
        var mouse = Mouse.current;
        if (bow && mouse != null && mouse.leftButton.isPressed && InControl(fpc))
            _bowDraw = Mathf.Min(1f, Mathf.Max(0f, _bowDraw) + Time.deltaTime / 0.65f);
        else _bowDraw = -1f;
        state.Bow = _bowDraw;
    }

    static void Watch(Animator hero, int layer, ref int lastHash, ref float lastTime)
    {
        if (layer < 0 || layer >= hero.layerCount) return;
        var st = hero.GetCurrentAnimatorStateInfo(layer);
        int hash = st.shortNameHash; float t = st.normalizedTime;
        bool started = hash != lastHash || t < lastTime - 0.3f;
        lastHash = hash; lastTime = t;
        if (started && _stateActions.TryGetValue(hash, out var action))
        {
            // The enemy clips wind up for longer than the hero's own quick swing: start inside the wind-up.
            bool cast = action == PuppetAction.CastR || action == PuppetAction.CastL, drink = action == PuppetAction.Drink;
            LastAction = action; LastDuration = Mathf.Clamp(st.length, 0.4f, drink ? 2f : 0.95f); LastSkip = drink ? 0f : cast ? 0.2f : 0.3f;
            ActionSeq++;
            Anim?.Play(action, LastDuration, LastSkip);
        }
    }

    static Dictionary<int, PuppetAction> BuildStateActions()
    {
        var d = new Dictionary<int, PuppetAction>();
        void Add(int hash, PuppetAction a) { if (hash != 0) d[hash] = a; }
        Add(PlayerCombat.HashSwordAttackRight1, PuppetAction.AttackR1); Add(PlayerCombat.HashSwordAttackRight2, PuppetAction.AttackR2);
        Add(PlayerCombat.HashSwordAttackLeft1, PuppetAction.AttackL1); Add(PlayerCombat.HashSwordAttackLeft2, PuppetAction.AttackL2);
        Add(PlayerCombat.HashShieldAttackRight1, PuppetAction.PunchR); Add(PlayerCombat.HashShieldAttackRight2, PuppetAction.PunchR);
        Add(PlayerCombat.HashShieldAttackLeft1, PuppetAction.PunchL); Add(PlayerCombat.HashShieldAttackLeft2, PuppetAction.PunchL);
        Add(PlayerCombat.HashAttackRight1, PuppetAction.PunchR); Add(PlayerCombat.HashAttackRight2, PuppetAction.PunchR);
        Add(PlayerCombat.HashAttackLeft1, PuppetAction.PunchL); Add(PlayerCombat.HashAttackLeft2, PuppetAction.PunchL);
        Add(PlayerCombat.HashStaffAttackRight1, PuppetAction.CastR); Add(PlayerCombat.HashStaffAttackLeft1, PuppetAction.CastL);
        foreach (var n in new[] { "Consume", "ConsumeRight", "ConsumeLeft" }) Add(Animator.StringToHash(n), PuppetAction.Drink);
        return d;
    }

    // ---- touches on top of the clips ----

    // Degrees at full weight; the game has no human crouch or jump clip, so the legs are bent by hand.
    internal static Vector4 CrouchPose = new(-70f, 115f, -40f, 12f), AirPose = new(-28f, 50f, 15f, 0f); // thigh, calf, foot, spine
    static readonly PuppetPoser _poser = new();

    // The game curls the first-person fingers around what the hand holds; the same skeleton, so the same
    // local rotations make the same grip on the puppet.
    static readonly Dictionary<string, (Transform src, Transform dst)[]> _fingers = new();
    internal static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinky" };

    static void CopyFingers(Transform fpHolder, bool holding, string side)
    {
        if (!holding || fpHolder == null || fpHolder.parent == null) return;
        if (!_fingers.TryGetValue(side, out var pairs))
        {
            var list = new List<(Transform, Transform)>();
            var hand = fpHolder.parent;
            foreach (var t in hand.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.EndsWith(side)) continue;
                bool finger = false;
                foreach (var f in FingerNames) if (t.name.StartsWith(f + "_0")) { finger = true; break; }
                var dst = finger ? Body.Bone(t.name) : null;
                if (dst != null) list.Add((t, dst));
            }
            _fingers[side] = pairs = list.ToArray();
        }
        foreach (var (src, dst) in pairs)
            if (src != null && dst != null) dst.localRotation = src.localRotation;
    }

    // ---- held items ----

    static readonly HeldItem _right = new(), _left = new();
    internal static void RetuneHeld() { _right.Retune(); _left.Retune(); }
    static Transform _limbo;

    // A copy of what a first-person hand holds, in the puppet's hand. The original keeps all game logic.
    // The first-person grip carries over to the puppet's hand as it is (Mad checked it in game: a half turn
    // put blades in a reverse grip). The per-kind turns stay as hooks for the dev "held" command.
    internal static Vector3 TurnWeapon = Vector3.zero, TurnStaff = Vector3.zero, TurnShield = Vector3.zero, TurnBow = Vector3.zero;

    internal static Vector3 TurnFor(GameObject held, bool right)
    {
        if (held.name.StartsWith("FSB_Bow")) return TurnBow;
        try
        {
            var inv = InventoryManager.Instance;
            var item = inv == null ? null : right ? inv.rightHandSlot?.item : inv.leftHandSlot?.item;
            string kind = item != null && _combat != null ? _combat.GetWeaponTypePrefix(item) : null;
            if (kind == PlayerCombat.PREFIX_SHIELD) return TurnShield;
            if (kind == PlayerCombat.PREFIX_STAFF) return TurnStaff;
        }
        catch { }
        return TurnWeapon;
    }

    sealed class HeldItem
    {
        IntPtr _srcPtr;
        GameObject _src, _clone;
        Transform _holder;
        Vector3 _turn;
        public bool Has => _clone != null && _src != null;
        public void Retune() => _srcPtr = IntPtr.Zero;

        public void Reset()
        {
            if (_clone != null) UnityEngine.Object.Destroy(_clone);
            if (_holder != null) UnityEngine.Object.Destroy(_holder.gameObject);
            _clone = null; _src = null; _holder = null; _srcPtr = IntPtr.Zero;
        }

        public void Sync(GameObject src, Transform fpHolder, Transform hand, bool right)
        {
            if (hand == null) return;
            if (src == null || fpHolder == null)
            {
                if (_clone != null) { UnityEngine.Object.Destroy(_clone); _clone = null; }
                _src = null; _srcPtr = IntPtr.Zero;
                return;
            }
            if (_holder == null)
            {
                _holder = new GameObject("FSC_Held").transform;
                _holder.SetParent(hand, false);
            }
            if (src.Pointer != _srcPtr || _clone == null)
            {
                if (_clone != null) UnityEngine.Object.Destroy(_clone);
                _srcPtr = src.Pointer; _src = src;
                _turn = TurnFor(src, right);
                _clone = MakeVisualCopy(src, _holder);
            }
            if (_clone == null) return;
            // Same bones in both skeletons: the local offsets carry over as they are.
            var turn = Quaternion.Euler(_turn);
            _holder.localPosition = turn * fpHolder.localPosition;
            _holder.localRotation = turn * fpHolder.localRotation;
            _holder.localScale = fpHolder.localScale;
            var st = src.transform; var ct = _clone.transform;
            ct.localPosition = st.localPosition; ct.localRotation = st.localRotation; ct.localScale = st.localScale;
            if (_clone.activeSelf != src.activeSelf) _clone.SetActive(src.activeSelf);
        }
    }

    internal static GameObject MakeVisualCopy(GameObject src, Transform parent)
    {
        try
        {
            if (_limbo == null)
            {
                // Cloned under an inactive parent, the copy's scripts never wake up before they are removed.
                var go = new GameObject("FSC_Limbo");
                go.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(go);
                _limbo = go.transform;
            }
            var clone = UnityEngine.Object.Instantiate(src, _limbo);
            clone.name = "FSC_" + src.name;
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in clone.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c.TryCast<Transform>() != null || c.TryCast<MeshFilter>() != null || c.TryCast<Renderer>() != null
                        || c.TryCast<ParticleSystem>() != null || c.TryCast<LODGroup>() != null) continue;
                    if (pass == 0 && c.TryCast<MonoBehaviour>() == null) continue; // scripts first: they may require the rest
                    UnityEngine.Object.DestroyImmediate(c);
                }
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true)) if (r != null) r.forceRenderingOff = false;
            clone.transform.SetParent(parent, false);
            return clone;
        }
        catch (Exception e) { Plugin.L.Warning("Held item copy failed: " + e.Message); return null; }
    }

    // ---- first-person visuals ----

    static void HideFirstPerson(bool hide)
    {
        if (_fpRoot == null) return;
        if (hide == _fpHidden && (!hide || Time.unscaledTime < _nextFpScan)) return;
        _nextFpScan = Time.unscaledTime + 0.25f;
        _fpHidden = hide;
        foreach (var r in _fpRoot.GetComponentsInChildren<Renderer>(true))
            if (r != null && r.forceRenderingOff != hide) r.forceRenderingOff = hide;
        // The Bows mod draws its string and nocked arrow as loose objects at the first-person hands.
        foreach (var n in new[] { "FSB_DrawnString", "FSB_NockedArrow" })
        {
            var go = GameObject.Find(n);
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r != null) r.forceRenderingOff = hide;
        }
    }

    // The editor may be opened in a dark dungeon: a soft lamp by the camera keeps the hero readable.
    static Light _lamp;
    static void PreviewLight(bool on)
    {
        if (!on) { if (_lamp != null && _lamp.enabled) _lamp.enabled = false; return; }
        if (_lamp == null)
        {
            var go = new GameObject("FSC_PreviewLamp");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _lamp = go.AddComponent<Light>();
            _lamp.type = LightType.Point;
            _lamp.range = 9f; _lamp.intensity = 1.1f;
            _lamp.color = new Color(1f, 0.95f, 0.86f);
            _lamp.shadows = LightShadows.None;
        }
        _lamp.enabled = true;
        var root = Body.Root.transform;
        var rot = Quaternion.Euler(OrbitPitch, root.eulerAngles.y + OrbitYaw, 0f);
        _lamp.transform.position = root.position + Vector3.up * 1.5f - rot * Vector3.forward * 2.2f;
    }

    // ---- camera, only for the duration of a render ----

    static bool _swapped;
    static Transform _camT;
    static Vector3 _savedPos;
    static Quaternion _savedRot;
    static float _dist = 2.5f, _aim = 20f;
    internal static float OrbitYaw = 160f, OrbitPitch = 8f, OrbitDist = 3.2f, OrbitHeight = 1.0f;

    // Layers by the game's own names. Walls and ground stop the camera; furniture and loot do not.
    static int _solidMask, _aimMask, _wallMask;
    static void Masks()
    {
        if (_solidMask != 0) return;
        _solidMask = LayerMask.GetMask("Default", "Terrain", "Floor", "Wall");
        _wallMask = LayerMask.GetMask("Terrain", "Floor", "Wall");
        _aimMask = _solidMask | LayerMask.GetMask("Enemy", "Interactable", "Shelf", "Destructible");
        if (_solidMask == 0) { _solidMask = 1; _aimMask = 1; }
    }

    // How far the camera can go from the eye before something solid is in the way. Fence rails, posts and
    // tree trunks do not count: a camera that jumps at every thin thing is worse than one that passes it.
    static float FreeDistance(Vector3 eye, Vector3 dir, float want, int mask)
    {
        float best = want;
        var hits = Physics.SphereCastAll(eye, 0.16f, dir, want, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            var col = h.collider;
            if (col == null || h.distance >= best) continue;
            if (col.gameObject.layer == 0)
            {
                var size = col.bounds.size;
                if (Mathf.Min(size.x, size.z) < 0.7f || size.y < 0.6f) continue;
            }
            best = h.distance;
        }
        return Mathf.Max(0.05f, best - 0.05f);
    }

    static bool _bodyHidden;
    static void SetBodyHidden(bool hidden)
    {
        if (hidden == _bodyHidden) return;
        _bodyHidden = hidden;
        foreach (var r in Body.Root.GetComponentsInChildren<Renderer>(true)) if (r != null) r.forceRenderingOff = hidden;
    }

    internal static void BeginRender()
    {
        RenderHookAlive = true;
        if (_swapped) EndRender();
        if (!_shown || Body == null || !Body.Alive) return;
        var fpc = FirstPersonController.Instance;
        if (fpc == null) return;
        var cam = fpc._mainCamera != null ? fpc._mainCamera : Camera.main;
        if (cam == null) return;
        _camT = cam.transform;
        _savedPos = _camT.position; _savedRot = _camT.rotation;
        Vector3 pos; Quaternion rot;
        float scale = Body.Root.transform.localScale.y;
        if (Preview)
        {
            var root = Body.Root.transform;
            // Close up the camera looks at the face, further away at the whole figure.
            OrbitHeight = Mathf.Lerp(1.55f, 0.95f, Mathf.InverseLerp(1.0f, 3.0f, OrbitDist));
            SetBodyHidden(false);
            Vector3 target = root.position + Vector3.up * (OrbitHeight * scale);
            rot = Quaternion.Euler(OrbitPitch, root.eulerAngles.y + OrbitYaw, 0f);
            // The editor panel covers the left of the screen: the hero stands in the free right part.
            target -= rot * Vector3.right * (OrbitDist * 0.3f);
            Masks();
            pos = target - rot * Vector3.forward * FreeDistance(target, -(rot * Vector3.forward), OrbitDist, _wallMask); // only real walls pull the editor camera in
        }
        else
        {
            Vector3 eye = _savedPos;
            Vector3 fwd = _savedRot * Vector3.forward;
            Masks();
            Vector3 offset = _savedRot * new Vector3(Settings.ShoulderRight.Value, Settings.ShoulderUp.Value, -Settings.Distance.Value) * scale;
            float want = offset.magnitude;
            Vector3 dir = offset / Mathf.Max(want, 1e-3f);
            float free = FreeDistance(eye, dir, want, _solidMask);
            // Pulled in at once (never through a wall), let out gently.
            _dist = free < _dist ? free : Mathf.MoveTowards(_dist, free, Time.unscaledDeltaTime * 6f);
            pos = eye + dir * _dist;
            SetBodyHidden(_dist < 0.55f * scale); // squeezed against a wall the camera would sit inside the head
            // The point the first-person view ray hits is the point the screen centre must show.
            float hitDist = Physics.Raycast(eye, fwd, out var aimHit, 150f, _aimMask, QueryTriggerInteraction.Ignore) ? aimHit.distance : 150f;
            hitDist = Mathf.Clamp(hitDist, 3f, 150f);
            _aim = Mathf.Lerp(_aim, hitDist, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            rot = Quaternion.LookRotation(eye + fwd * _aim - pos, _savedRot * Vector3.up);
        }
        _camT.SetPositionAndRotation(pos, rot);
        _swapped = true;
    }

    internal static void EndRender()
    {
        if (!_swapped) return;
        _swapped = false;
        if (_camT != null) _camT.SetPositionAndRotation(_savedPos, _savedRot);
    }
}

[HarmonyPatch(typeof(ProceduralHandAnimator), nameof(ProceduralHandAnimator.LateUpdate))]
internal static class HandAnimatorLatePatch
{
    static void Postfix()
    {
        ThirdPerson.LateHookAlive = true;
        try { ThirdPerson.LateTick(); } catch (Exception e) { Plugin.Fail("Body update", e); }
    }
}

[HarmonyPatch(typeof(UnityEngine.Rendering.RenderPipelineManager), nameof(UnityEngine.Rendering.RenderPipelineManager.BeginContextRendering))]
internal static class RenderBeginPatch
{
    static void Prefix()
    {
        try { ThirdPerson.BeginRender(); } catch (Exception e) { Plugin.Fail("Camera", e); }
    }
}

[HarmonyPatch(typeof(UnityEngine.Rendering.RenderPipelineManager), nameof(UnityEngine.Rendering.RenderPipelineManager.EndContextRendering))]
internal static class RenderEndPatch
{
    static void Postfix()
    {
        try { ThirdPerson.EndRender(); } catch (Exception e) { Plugin.Fail("Camera", e); }
    }
}
