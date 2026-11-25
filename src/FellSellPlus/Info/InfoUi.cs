using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FellSellPlus.Info;

// Small UI kit shared by the Info screens: one overlay canvas per screen, the game's TMP font,
// the dialog sprite of an existing game window, and modal open/close with input blocking.
internal static class InfoUi
{
    public static readonly Color Ink = new(0.93f, 0.89f, 0.80f, 1f);
    public static readonly Color Muted = new(0.70f, 0.65f, 0.56f, 1f);
    public static readonly Color Gold = new(1f, 0.82f, 0.40f, 1f);
    public static readonly Color Good = new(0.55f, 0.90f, 0.50f, 1f);
    public static readonly Color Bad = new(1f, 0.45f, 0.40f, 1f);
    static readonly Color PanelFallback = new(0.08f, 0.07f, 0.06f, 0.92f);

    public static bool Ru => Progression.GameQuery.IsRussian();
    public static string T(string ru, string en) => Ru ? ru : en;

    public static TMP_FontAsset Font()
    {
        var f = FirstPersonController.Instance?.interactionText?.font;
        if (f != null) return f;
        var any = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
        return any != null ? any.font : null;
    }

    static Sprite PanelSprite(out Image.Type type, out Color tint)
    {
        type = Image.Type.Sliced; tint = Color.white;
        var img = GrandmaShrineUI._instance?._dialogWindow?.GetComponent<Image>();
        if (img == null || img.sprite == null) return null;
        type = img.type; tint = img.color; // the sprite is white; the game tints it via the Image colour
        return img.sprite;
    }

    public static GameObject Canvas(string name, int order)
    {
        var root = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(root);
        var c = root.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = order;
        var s = root.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        s.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    public static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null, float alpha = 1f)
    {
        var rt = Rect(name, parent, anchor, pos, size, pivot);
        var img = rt.gameObject.AddComponent<Image>();
        var sp = PanelSprite(out var type, out var tint);
        if (sp != null) { img.sprite = sp; img.type = type; img.color = new Color(tint.r, tint.g, tint.b, tint.a * alpha); }
        else img.color = new Color(PanelFallback.r, PanelFallback.g, PanelFallback.b, PanelFallback.a * alpha);
        img.raycastTarget = false;
        return rt;
    }

    // A rich-text block filling its parent with a margin.
    public static TextMeshProUGUI Text(Transform parent, string name, float size, TextAlignmentOptions align, float margin = 24f)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin); rt.offsetMax = new Vector2(-margin, -margin);
        var t = go.AddComponent<TextMeshProUGUI>();
        var f = Font();
        if (f != null) t.font = f;
        t.fontSize = size; t.color = Ink; t.alignment = align;
        t.richText = true; t.enableWordWrapping = true; t.raycastTarget = false;
        TextFit.Block(t, size);
        return t;
    }

    public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    public static string C(Color c, string s) => $"<color={Hex(c)}>{s}</color>";

    public static string RarityHex(int r) => r switch
    {
        0 => "#C8C8C8", 1 => "#6FD36F", 2 => "#5AA8FF", 3 => "#C07BFF", 4 => "#FFB13B", _ => "#FF4A4A",
    };

    public static string RarityName(int r) => r switch
    {
        0 => T("Обычные", "Common"), 1 => T("Необычные", "Uncommon"), 2 => T("Редкие", "Rare"),
        3 => T("Эпические", "Epic"), 4 => T("Легендарные", "Legendary"), _ => T("Богохульные", "Blasphemous"),
    };

    public static string Time(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    // ---- modal windows ----
    static CursorLockMode _prevLock;
    static bool _prevVisible;
    static int _modalDepth;

    public static void PushModal()
    {
        if (_modalDepth++ > 0) return;
        _prevLock = Cursor.lockState; _prevVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        InputHandler()?.SetInputsBlocked(true, false, false);
    }

    public static void PopModal()
    {
        if (_modalDepth == 0 || --_modalDepth > 0) return;
        Cursor.lockState = _prevLock; Cursor.visible = _prevVisible;
        InputHandler()?.SetInputsBlocked(false, false, false);
    }

    static PlayerInputHandler InputHandler() =>
        FirstPersonController.Instance != null ? FirstPersonController.Instance.playerInputHandler : null;

    public static bool Pressed(Key k)
    {
        var kb = Keyboard.current;
        return kb != null && kb[k].wasPressedThisFrame;
    }

    public static bool BackPressed()
    {
        var kb = Keyboard.current; var pad = Gamepad.current;
        // Esc belongs to the game's window stack (it closes the window); Backspace steps back inside it.
        return (kb != null && kb.backspaceKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
    }

    public static Key ParseKey(string name, Key fallback) =>
        Enum.TryParse<Key>(name, true, out var k) && k != Key.None ? k : fallback;
}
