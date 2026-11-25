using System.Collections.Generic;
using Il2CppEmeraldAI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FellSellPlus.Info;

// Health bar over every enemy's head. EmeraldAI ships its own bars, but the game builds enemies
// without them, so the mod draws its own on one overlay canvas: a dark plate, a red fill and a pale
// trail that drains after a hit. Bars hide behind walls (line of sight), past the range and on death.
internal static class EnemyBars
{
    sealed class Bar
    {
        public EmeraldHealth Health;
        public Collider Body;
        public RectTransform Root, Fill, Trail;
        public float Shown = 1f, TrailValue = 1f, NextLos;
        public bool Visible;
    }

    const float W = 96f, H = 9f;
    static readonly Color Plate = new(0.08f, 0.05f, 0.04f, 0.85f);
    static readonly Color Red = new(0.80f, 0.16f, 0.12f, 1f);
    static readonly Color Pale = new(0.98f, 0.82f, 0.55f, 0.9f);

    static GameObject _canvas;
    static readonly Dictionary<System.IntPtr, Bar> Bars = new();
    static float _nextScan;

    static bool InRun()
    {
        var s = SceneManager.GetActiveScene().name;
        return s == "DUNGEON" || s == "FOREST";
    }

    public static void Tick()
    {
        bool on = InfoSettings.EnemyHealthBars.Value && InRun();
        if (!on) { Clear(); return; }
        var cam = Camera.main;
        if (cam == null) return;
        if (_canvas == null) _canvas = Shop.Ui.Canvas("FSP_EnemyBars", -5);

        if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 1f; Scan(); }

        float range = InfoSettings.EnemyHealthBarRange.Value;
        var dead = new List<System.IntPtr>();
        foreach (var kv in Bars)
        {
            var b = kv.Value;
            if (b.Health == null || b.Root == null) { dead.Add(kv.Key); continue; }
            float max = Mathf.Max(1f, b.Health.StartingHealth);
            float hp = Mathf.Clamp01(b.Health.CurrentHealth / max);
            Vector3 head = Head(b);
            Vector3 sp = cam.WorldToScreenPoint(head);
            float dist = Vector3.Distance(cam.transform.position, head);

            bool show = hp > 0f && sp.z > 0f && dist <= range
                        && !(InfoSettings.EnemyHealthBarsOnlyDamaged.Value && hp >= 0.999f);
            if (show && Time.unscaledTime >= b.NextLos)
            {
                b.NextLos = Time.unscaledTime + 0.15f;
                b.Visible = Sees(cam.transform.position, head, b.Health.transform);
            }
            show &= b.Visible;
            if (b.Root.gameObject.activeSelf != show) b.Root.gameObject.SetActive(show);
            if (!show) continue;

            // Screen pixels to canvas units (the scaler sets scaleFactor).
            float k = 1f / Mathf.Max(0.01f, _canvas.GetComponent<Canvas>().scaleFactor);
            b.Root.anchoredPosition = new Vector2(sp.x * k, sp.y * k);
            float scale = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(4f, range, dist));
            b.Root.localScale = new Vector3(scale, scale, 1f);

            b.Shown = hp;
            b.TrailValue = hp >= b.TrailValue ? hp : Mathf.MoveTowards(b.TrailValue, hp, Time.deltaTime * 0.6f);
            b.Fill.sizeDelta = new Vector2(W * b.Shown, H);
            b.Trail.sizeDelta = new Vector2(W * b.TrailValue, H);
        }
        foreach (var p in dead) { if (Bars[p].Root != null) Object.Destroy(Bars[p].Root.gameObject); Bars.Remove(p); }
    }

    static void Scan()
    {
        foreach (var h in Object.FindObjectsOfType<EmeraldHealth>())
        {
            if (h == null || Bars.ContainsKey(h.Pointer)) continue;
            Bars[h.Pointer] = Make(h);
        }
    }

    static Vector3 Head(Bar b)
    {
        if (b.Body != null && b.Body.enabled) { var bd = b.Body.bounds; return new Vector3(bd.center.x, bd.max.y + 0.35f, bd.center.z); }
        return b.Health.transform.position + Vector3.up * 2.2f;
    }

    // Line of sight from the camera to the head; the enemy's own colliders do not block it.
    static bool Sees(Vector3 from, Vector3 to, Transform enemy)
    {
        var dir = to - from;
        if (!Physics.Raycast(from, dir.normalized, out var hit, dir.magnitude - 0.2f, ~0, QueryTriggerInteraction.Ignore)) return true;
        var t = hit.collider != null ? hit.collider.transform : null;
        return t != null && (t == enemy || t.IsChildOf(enemy) || enemy.IsChildOf(t.root) && t.root != t);
    }

    static Bar Make(EmeraldHealth h)
    {
        var b = new Bar { Health = h, Body = h.GetComponent<Collider>() };
        var root = new GameObject("FSP_EnemyBar"); root.AddComponent<RectTransform>();
        root.transform.SetParent(_canvas.transform, false);
        b.Root = root.GetComponent<RectTransform>();
        b.Root.anchorMin = b.Root.anchorMax = Vector2.zero;
        b.Root.pivot = new Vector2(0.5f, 0f);
        b.Root.sizeDelta = new Vector2(W + 4f, H + 4f);

        Rect(root.transform, "Plate", Plate, new Vector2(W + 4f, H + 4f), center: true);
        b.Trail = Rect(root.transform, "Trail", Pale, new Vector2(W, H), center: false);
        b.Fill = Rect(root.transform, "Fill", Red, new Vector2(W, H), center: false);
        // A thin light line on the top edge of the fill gives the bar some volume.
        var gloss = Rect(b.Fill, "Gloss", new Color(1f, 0.55f, 0.45f, 0.55f), new Vector2(0f, 2f), center: false);
        gloss.anchorMin = new Vector2(0f, 1f); gloss.anchorMax = new Vector2(1f, 1f); gloss.pivot = new Vector2(0f, 1f);
        gloss.anchoredPosition = Vector2.zero; gloss.sizeDelta = new Vector2(0f, 2f);
        root.SetActive(false);
        return b;
    }

    static RectTransform Rect(Transform parent, string name, Color c, Vector2 size, bool center)
    {
        var go = new GameObject(name); go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        if (center)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
        else
        {
            // Left-anchored so the width is the health fraction.
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(2f, 0f);
        }
        rt.sizeDelta = size;
        return rt;
    }

    static void Clear()
    {
        if (Bars.Count == 0) return;
        foreach (var b in Bars.Values) if (b.Root != null) Object.Destroy(b.Root.gameObject);
        Bars.Clear();
    }
}
