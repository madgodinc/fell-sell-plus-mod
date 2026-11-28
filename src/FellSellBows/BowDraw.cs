using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FellSellBows;

// First-person bow: idle hold, hold-to-draw on the left button, loose, re-nock.
//
// Everything is posed in camera space, so it is rigid with the view and cannot twitch:
//   * the bow has a fixed place in front of the camera, upright with a slight cant; the arrow's line
//     starts low on the right and crosses the view axis ahead, so on screen it points at the crosshair;
//   * the left hand keeps the game's own staff grip: its rotation is derived from the bow's, the arm
//     reaches the wrist point by two-bone IK;
//   * the right hand hooks the string at the nock and pulls it back; the string line and the nocked
//     arrow follow it.
// The pose is applied right after the game's ProceduralHandAnimator.LateUpdate (which animates and
// hides the arms); applied any earlier, the game switches the empty right arm off again.
internal static class BowDraw
{
    // Pose. Camera space: x right, y up, z forward, metres.
    internal static Vector3 ArrowLine = new(0.11f, -0.12f, 0.36f); // the nock (arrow's tail) at rest
    internal static float Converge = 1.5f;      // the arrow's line meets the view axis this far ahead, so on screen it points at the crosshair
    internal static float PushZ = 0.05f;        // the bow moves this much forward as the draw starts
    internal static float DrawLength = 0.27f;   // how far the string is pulled at full draw
    internal static float Cant = 20f;           // degrees, the top limb leans right
    internal static float ArrowRest = 0.045f;   // the arrow above the grip's centre
    internal static float ArrowSide = 0.03f;    // the arrow left of the bow's middle plane
    internal static float StringZ = 0.05f;      // the string's plane inside the mesh depth (0 back .. 1 belly)
    internal static float BowScale = 1f, ArrowScale = 0.85f;
    internal static Vector3 PoleL = new(-0.6f, -0.7f, 0.1f), PoleR = new(0.8f, -0.4f, -0.4f);
    // String hand, in the bow's frame (x right, y up along the limbs, z toward the target):
    internal static Vector3 FingersDir = new(-0.94f, 0f, 0.34f);  // wrist to knuckles
    internal static Vector3 PalmDir = new(-0.34f, 0f, -0.94f);    // palm normal
    internal static Vector3 WristFromNock = new(0.0841f, -0.0047f, -0.0583f); // Mad's hand pose
    internal static Vector3 ThumbTurn = Vector3.zero;  // thumb_01_r extra turn about the hand's axes
    internal static float ThumbCurl = 0f;              // thumb joints 2..3 bend, degrees
    internal static Vector3 HandTurn = new(208.07f, 23.25f, 0f); // extra turn of the string hand (bow frame euler)
    internal static Vector3 Curl = new(32.5f, 87.5f, 57.5f);            // finger joints 1..3, degrees
    internal static Vector3 ReloadDip = new(0.07f, -0.09f, -0.03f);
    static float _sprintW;
    internal static float Sway = 1f;            // share of the game's own hand bob applied to the bow
    internal static float MinDraw = 0.3f;       // released earlier: no shot
    internal static float DrawTime = 0.65f;     // seconds to full draw at weaponAttackSpeed 1
    internal static float ReloadTime = 0.25f;

    const float LooseSnap = 0.05f, LooseHold = 0.10f, CancelTime = 0.2f;

    // Draw state: advanced in Update from the input, posed in LateUpdate.
    enum Phase { Idle, Draw, Loose, Cancel }
    static Phase _phase;
    static float _raw, _phaseStart, _rawAtEnd, _drawLen = 0.5f, _reload = 0.45f, _fireTime;
    internal static bool Firing;
    // The staff attack spawns its projectile from a coroutine a frame or two after the release.
    internal static bool JustFired => Time.time - _fireTime < 0.25f;
    internal static float LastPull = 1f; // how far the string was drawn for the last shot (0..1)

    static float Smooth(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));
    static float PushOf(float raw) => Smooth(raw / 0.22f);
    static float PullOf(float raw) { float x = Mathf.Clamp01((raw - 0.1f) / 0.9f); return 1f - (1f - x) * (1f - x); }

    static bool DrawHeld()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        return mouse != null && mouse.leftButton.isPressed;
    }

    // Every attack speed bonus the hero has, without the bow's own speed: the weapon factor
    // (GetWeaponSpeedFactor with the item's speed set to 1) times PlayerStats._attackSpeedModifier.
    // Gear bonuses (the bow's own included) are only in the latter.
    static PlayerStats _stats;
    static float AttackSpeedMultiplier(ItemData item)
    {
        if (item == null) return 1f;
        try
        {
            var combat = Hands != null ? Hands._playerCombat : UnityEngine.Object.FindObjectOfType<PlayerCombat>();
            if (combat == null) return 1f;
            float own = item.weaponAttackSpeed;
            item.weaponAttackSpeed = 1f;
            float f;
            try { f = combat.GetWeaponSpeedFactor(item); }
            finally { item.weaponAttackSpeed = own; }
            if (!(f > 0.2f && f < 5f)) f = 1f;
            if (_stats == null || !_stats) _stats = UnityEngine.Object.FindObjectOfType<PlayerStats>();
            float hero = _stats != null ? _stats._attackSpeedModifier : 1f;
            if (!(hero > 0.2f && hero < 5f)) hero = 1f;
            return f * hero;
        }
        catch { return 1f; }
    }

    // The player is in control of the hero (no menu, alive, not paused).
    static bool CanAct()
    {
        if (Time.timeScale <= 0f || Cursor.lockState != CursorLockMode.Locked) return false;
        var fpc = FirstPersonController.Instance;
        if (fpc == null || !fpc.canLook || fpc._isDead) return false;
        var input = PlayerInputHandler.Instance;
        if (input != null && input._isInputsBlocked && !input._isCombatAllowedWhileBlocked) return false;
        return Hands == null || Hands._leftVisibilityWeight > 0.5f;
    }

    internal static void InputTick()
    {
        var eq = PlayerEquipment.Instance;
        if (eq == null || !TwoHanded.IsBow(eq.currentLeftHandItem)) { _phase = Phase.Idle; _raw = 0f; return; }
        bool button = DrawHeld(), can = CanAct();
        float now = Time.time;
        switch (_phase)
        {
            case Phase.Loose:
                // Mad: no reload, the draw is the reload; holding again starts the next draw at once.
                if (button && can && now - _phaseStart >= LooseHold) goto case Phase.Idle;
                if (now - _phaseStart >= LooseHold + _reload) { _phase = Phase.Idle; _raw = 0f; }
                break;
            case Phase.Idle:
                if (button && can)
                {
                    var item = InventoryManager.Instance?.leftHandSlot?.item;
                    float speed = item != null && item.weaponAttackSpeed > 0f ? item.weaponAttackSpeed : 1.3f;
                    // Mad: the heavy bow draws in about 1.5 s before attack speed bonuses.
                    _drawLen = Mathf.Clamp(DrawTime / speed, 0.35f, 1.5f);
                    _drawLen = Mathf.Max(0.25f, _drawLen / AttackSpeedMultiplier(item));
                    _reload = ReloadTime;
                    _phase = Phase.Draw; _phaseStart = now; _raw = 0f;
                }
                break;
            case Phase.Draw:
                _raw = Mathf.Clamp01((now - _phaseStart) / _drawLen);
                if (button && can) break;
                _rawAtEnd = _raw; _phaseStart = now;
                if (!button && can && _raw >= MinDraw) { _phase = Phase.Loose; LastPull = _raw; Fire(); }
                else _phase = Phase.Cancel;
                break;
            case Phase.Cancel:
                if (now - _phaseStart >= CancelTime) { _phase = Phase.Idle; _raw = 0f; }
                break;
        }
    }

    // The shot itself is the game's: the staff attack spawns the projectile (re-skinned as an arrow).
    static void Fire()
    {
        try
        {
            var combat = Hands != null ? Hands._playerCombat : null;
            if (combat == null) combat = UnityEngine.Object.FindObjectOfType<PlayerCombat>();
            if (combat == null) { Plugin.L.Warning("Bow: no PlayerCombat, the arrow was not fired."); return; }
            var item = InventoryManager.Instance?.leftHandSlot?.item;
            _fireTime = Time.time;
            // The draw itself was the wait: the staff's own cooldown and slow swing must not delay the arrow.
            float speed = item != null ? item.weaponAttackSpeed : 0f;
            Firing = true;
            try
            {
                combat.lastLeftAttackTime = -100f;
                if (item != null && speed < 1.3f) item.weaponAttackSpeed = 1.3f;
                combat.ExecuteLeftHandAttack();
            }
            finally { Firing = false; if (item != null) item.weaponAttackSpeed = speed; }
        }
        catch (Exception e) { Plugin.L.Error("Bow shot: " + e.Message); }
    }

    internal static bool HookAlive;
    internal static ProceduralHandAnimator Hands;
    static bool _active;
    static readonly Dictionary<IntPtr, (Camera c, float n)> _nearOrigs = new();
    static readonly List<SkinnedMeshRenderer> _leftParts = new(), _rightParts = new();
    static IntPtr _partsRoot;

    // The held bow and its holder as the game placed them (the staff attack clip jabs them; they are reset every frame).
    static IntPtr _heldPtr;
    static Quaternion _heldRot, _propRot;
    static Vector3 _heldPos, _heldScale, _propPos;

    static readonly List<(Transform t, int joint)> _fingers = new();
    static IntPtr _fingersFor;
    static readonly Dictionary<IntPtr, (Quaternion baseRot, Quaternion applied)> _track = new();

    static void Deactivate()
    {
        if (!_active) return;
        _active = false;
        BowString.Hide();
        bool show = Hands != null && Hands._rightVisibilityWeight > 0.01f;
        foreach (var r in _rightParts) if (r != null) r.enabled = show;
        foreach (var kv in _nearOrigs) if (kv.Value.c != null) kv.Value.c.nearClipPlane = kv.Value.n;
        _nearOrigs.Clear();
        _heldPtr = IntPtr.Zero;
        // Fingers nothing else animates would stay curled: give them back as they were.
        foreach (var (t, _) in _fingers)
            if (t != null && _track.TryGetValue(t.Pointer, out var tr) && Quaternion.Angle(t.localRotation, tr.applied) < 0.01f) t.localRotation = tr.baseRot;
        _track.Clear();
    }

    static void FindParts(Transform root)
    {
        if (root == null || root.Pointer == _partsRoot && _leftParts.Count > 0) return;
        _partsRoot = root.Pointer; _leftParts.Clear(); _rightParts.Clear();
        foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r == null) continue;
            if (r.name.Contains("AUPL") || r.name.Contains("ALWL") || r.name.Contains("HNDL")) _leftParts.Add(r);
            else if (r.name.Contains("AUPR") || r.name.Contains("ALWR") || r.name.Contains("HNDR")) _rightParts.Add(r);
        }
    }

    static void FindFingers(Transform handR)
    {
        if (_fingersFor == handR.Pointer) return;
        _fingersFor = handR.Pointer; _fingers.Clear(); _track.Clear();
        foreach (var f in new[] { "index", "middle", "ring", "pinky" })
            for (int j = 1; j <= 3; j++)
                foreach (var t in handR.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == $"{f}_0{j}_r") { _fingers.Add((t, j)); break; }
        for (int j = 1; j <= 3; j++)
            foreach (var t in handR.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == $"thumb_0{j}_r") { _fingers.Add((t, 10 + j)); break; }
    }

    internal static void LateTick()
    {
        var eq = PlayerEquipment.Instance;
        var cam = Camera.main;
        var held = eq != null && TwoHanded.IsBow(eq.currentLeftHandItem) ? eq.currentLeftHandItem : null;
        if (cam == null || held == null || eq.leftHandHolder == null || eq.rightHandHolder == null) { Deactivate(); return; }
        var prop = eq.leftHandHolder;
        var hand = prop.parent;                           // hand_l
        var fore = hand != null ? hand.parent : null;     // lowerarm_l
        var upper = fore != null ? fore.parent : null;    // upperarm_l
        var handR = eq.rightHandHolder.parent;
        var foreR = handR != null ? handR.parent : null;
        var upperR = foreR != null ? foreR.parent : null;
        var mf = held.GetComponentInChildren<MeshFilter>();
        if (upper == null || upperR == null || mf == null || mf.sharedMesh == null) { Deactivate(); return; }
        var mt = mf.transform;
        var bowRenderer = mf.GetComponent<Renderer>();
        _active = true;
        FindParts(upper.root);
        FindFingers(handR);

        // The string hand comes close to the eye: cameras draw from 2 cm while a bow is held.
        foreach (var c in Camera.allCameras)
        {
            if (c == null || !c.enabled || c.nearClipPlane <= 0.02f) continue;
            if (!_nearOrigs.ContainsKey(c.Pointer)) _nearOrigs[c.Pointer] = (c, c.nearClipPlane);
            c.nearClipPlane = 0.02f;
        }

        // The game shows only the arm that holds something. The string arm shows whenever the bow arm does
        // (the game still hides both in menus).
        bool shown = _leftParts.Count == 0 || _leftParts[0].enabled;
        foreach (var r in _rightParts) if (r != null) { r.enabled = shown; r.updateWhenOffscreen = true; }
        foreach (var r in _leftParts) if (r != null) r.updateWhenOffscreen = true;

        float now = Time.time, since = now - _phaseStart;
        float push = 0f, pull = 0f, handPull = 0f, curl = 1f, vib = 0f, dip = 0f;
        bool arrowOnString = true, arrowInHand = false;
        switch (_phase)
        {
            case Phase.Draw:
                push = PushOf(_raw); pull = handPull = PullOf(_raw);
                break;
            case Phase.Cancel:
            {
                float k = 1f - Smooth(since / CancelTime);
                push = PushOf(_rawAtEnd) * k; pull = handPull = PullOf(_rawAtEnd) * k;
                break;
            }
            case Phase.Loose:
            {
                float p0 = PullOf(_rawAtEnd);
                pull = p0 * Mathf.Clamp01(1f - since / LooseSnap);
                vib = since > LooseSnap ? 0.02f * p0 * Mathf.Sin((since - LooseSnap) * 95f) * Mathf.Exp(-(since - LooseSnap) * 11f) : 0f;
                push = PushOf(_rawAtEnd) * (1f - Smooth(since / 0.35f));
                float u = Mathf.Clamp01((since - LooseHold) / _reload);
                handPull = p0 * (1f - Smooth(u));
                dip = Mathf.Sin(u * Mathf.PI);
                curl = since < LooseHold ? 0.25f : Mathf.Lerp(0.25f, 1f, Smooth(u / 0.4f));
                arrowOnString = false; arrowInHand = u > 0.4f;
                break;
            }
        }

        // The view frame: the camera, shifted by the game's own hand bob and its hide slide.
        var ct = cam.transform;
        Vector3 origin = ct.position;
        float visible = 1f;
        if (Hands != null)
        {
            visible = Mathf.Clamp01(Hands._leftVisibilityWeight);
            var tgt = Hands._leftHandTarget;
            // Mad: no arm sway while sprinting; the game's sprint bob fades out over ~0.15 s.
            var fpcS = FirstPersonController.Instance;
            _sprintW = Mathf.MoveTowards(_sprintW, fpcS != null && fpcS.IsSprinting ? 1f : 0f, Time.deltaTime / 0.15f);
            if (tgt != null && tgt.parent != null) origin += (tgt.position - tgt.parent.position) * (Sway * (1f - 0.6f * pull) * (1f - _sprintW));
            origin -= ct.up * ((1f - visible) * 1.0f);
        }
        Vector3 aim = (new Vector3(0f, 0f, Mathf.Max(Converge, ArrowLine.z + 0.5f)) - ArrowLine).normalized; // camera space
        Quaternion bowRot = ct.rotation * Quaternion.LookRotation(aim, Vector3.up) * Quaternion.AngleAxis(-Cant, Vector3.forward);
        Vector3 fwd = bowRot * Vector3.forward, up = bowRot * Vector3.up, right = Vector3.Cross(up, fwd);

        // The bow in the fist, as the game holds a staff. The staff attack clip jabs it, so it is reset every frame.
        if (_heldPtr != held.Pointer)
        {
            _heldPtr = held.Pointer;
            _heldRot = held.transform.localRotation; _heldPos = held.transform.localPosition; _heldScale = held.transform.localScale;
            _propRot = prop.localRotation; _propPos = prop.localPosition;
        }
        prop.localRotation = _propRot; prop.localPosition = _propPos;
        held.transform.localScale = _heldScale * BowScale;
        held.transform.localRotation = _heldRot;
        held.transform.localPosition = _heldPos;

        var b = mf.sharedMesh.bounds;
        float lossy = mt.lossyScale.y;
        // Where the string meets the limbs: the model's own anchor, else from the mesh bounds.
        float stringLocalZ = b.min.z + b.size.z * StringZ, tipY = b.extents.y * 0.97f;
        Vector3 mid = b.center;
        if (BowArt.TryStringAnchor(mf.sharedMesh, out float anchorY, out float anchorZ)) { tipY = anchorY; stringLocalZ = anchorZ; mid = new Vector3(0f, 0f, b.center.z); }
        // Grip so that the arrow's axis lies on ArrowLine and the nock rests at ArrowLine.z (plus the push).
        Vector3 nockRest = origin + ct.rotation * (ArrowLine + aim * (PushZ * push));
        Vector3 stringMidRest = nockRest - up * ArrowRest + right * ArrowSide;
        Vector3 grip = stringMidRest + fwd * ((mid.z - stringLocalZ) * lossy);

        // Left arm: hand rotation from the bow's, wrist from the grip point.
        Quaternion handToBow = Quaternion.Inverse(hand.rotation) * mt.rotation;
        Quaternion handRot = bowRot * Quaternion.Inverse(handToBow);
        Vector3 gripInHand = hand.InverseTransformPoint(mt.TransformPoint(mid));
        Vector3 wrist = grip - handRot * Vector3.Scale(gripInHand, hand.lossyScale);
        Solve(upper, fore, hand, wrist, origin + ct.rotation * PoleL);
        hand.rotation = handRot;

        // String and arrow, from where the bow really is.
        Vector3 top = mt.TransformPoint(new Vector3(mid.x, mid.y + tipY, stringLocalZ));
        Vector3 bottom = mt.TransformPoint(new Vector3(mid.x, mid.y - tipY, stringLocalZ));
        Vector3 bowFwd = mt.forward, bowUp = mt.up, bowRight = Vector3.Cross(bowUp, bowFwd);
        Vector3 nock0 = (top + bottom) * 0.5f + bowUp * ArrowRest - bowRight * ArrowSide;
        Vector3 nock = nock0 - bowFwd * (DrawLength * pull - vib);
        Vector3 fingerPoint = nock0 - bowFwd * (DrawLength * handPull) + (bowRight * ReloadDip.x + bowUp * ReloadDip.y + bowFwd * ReloadDip.z) * dip;
        if (_phase != Phase.Loose) fingerPoint = nock;

        // Right arm: fingers hooked on the string at the nock.
        Quaternion frame = Quaternion.LookRotation(bowFwd, bowUp);
        var turned = frame * Quaternion.Euler(HandTurn);
        Vector3 fingers = (turned * FingersDir).normalized, palm = (turned * PalmDir).normalized;
        palm = (palm - fingers * Vector3.Dot(palm, fingers)).normalized;
        // hand_r: +x along the fingers, +y out of the palm, -z toward the thumb.
        Quaternion handRRot = Quaternion.LookRotation(Vector3.Cross(fingers, palm), palm);
        Vector3 wristR = fingerPoint + frame * WristFromNock;
        Solve(upperR, foreR, handR, wristR, origin + ct.rotation * PoleR);
        handR.rotation = handRRot;
        Vector3 curlAxis = handR.forward;
        foreach (var (t, joint) in _fingers)
        {
            if (t == null) continue;
            var cur = t.localRotation;
            if (_track.TryGetValue(t.Pointer, out var tr) && Quaternion.Angle(cur, tr.applied) < 0.01f) cur = tr.baseRot; // ours from last frame, not the game's
            t.localRotation = cur;
            if (joint == 11) t.rotation = handR.rotation * Quaternion.Euler(ThumbTurn) * Quaternion.Inverse(handR.rotation) * t.rotation;
            else if (joint > 11) t.rotation = Quaternion.AngleAxis(ThumbCurl, t.forward) * t.rotation;
            else t.rotation = Quaternion.AngleAxis(Curl[joint - 1] * curl, curlAxis) * t.rotation;
            _track[t.Pointer] = (cur, t.localRotation);
        }

        if (shown && visible > 0.05f)
            BowString.Show(bowRenderer, top, nock, bottom, arrowOnString || arrowInHand, arrowOnString ? nock : fingerPoint, bowFwd, ArrowScale, false);
        else BowString.Hide();
    }

    // Two-bone IK: rotate a (upper arm) and b (forearm) so c (hand) reaches target, elbow toward pole.
    static void Solve(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
    {
        Vector3 pa = a.position, pb = b.position, pc = c.position;
        float lab = (pb - pa).magnitude, lcb = (pc - pb).magnitude;
        if (lab < 1e-4f || lcb < 1e-4f) return;
        float lat = Mathf.Clamp((target - pa).magnitude, 1e-3f, lab + lcb - 1e-3f);

        float acAb0 = Angle(pc - pa, pb - pa);
        float baBc0 = Angle(pa - pb, pc - pb);
        float acAt0 = Angle(pc - pa, target - pa);
        float acAb1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float baBc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

        Vector3 axis0 = Vector3.Cross(pc - pa, pole - pa).normalized;
        if (axis0.sqrMagnitude < 1e-6f) axis0 = Vector3.Cross(pc - pa, pb - pa).normalized;
        Vector3 axis1 = Vector3.Cross(pc - pa, target - pa).normalized;

        var aRot = a.rotation; var bRot = b.rotation;
        var q0 = Quaternion.AngleAxis((acAb1 - acAb0) * Mathf.Rad2Deg, axis0);
        var q1 = Quaternion.AngleAxis((baBc1 - baBc0) * Mathf.Rad2Deg, axis0);
        a.rotation = q0 * aRot;
        b.rotation = q0 * q1 * bRot; // the forearm keeps the upper arm's turn plus its own bend
        if (axis1.sqrMagnitude > 1e-6f)
            a.rotation = Quaternion.AngleAxis(acAt0 * Mathf.Rad2Deg, axis1) * a.rotation;
    }

    static float Angle(Vector3 u, Vector3 v) => Mathf.Acos(Mathf.Clamp(Vector3.Dot(u.normalized, v.normalized), -1f, 1f));
}

// The game's hand animator moves, hides and shows the arms in its LateUpdate; the bow pose goes on right after it.
[HarmonyPatch(typeof(ProceduralHandAnimator), nameof(ProceduralHandAnimator.LateUpdate))]
internal static class HandAnimatorPatch
{
    static void Postfix(ProceduralHandAnimator __instance)
    {
        BowDraw.HookAlive = true; BowDraw.Hands = __instance;
        try { BowDraw.LateTick(); } catch (Exception e) { Plugin.L.Error("Bow draw: " + e.Message); }
    }
}
