using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FellSellPlus.Shop;

// Small builder for code-made UI that borrows the game's look (prompt font, Grandma's shrine panel
// and button sprites). Shared by the Shop and Crafting modules.
internal static class Ui
{
    public static readonly Color Ink = new(0.93f, 0.89f, 0.80f, 1f);
    public static readonly Color Muted = new(0.70f, 0.65f, 0.56f, 1f);
    public static readonly Color GoldInk = new(1f, 0.82f, 0.40f, 1f);
    public static readonly Color Good = new(0.55f, 0.85f, 0.45f, 1f);
    public static readonly Color Warn = new(1f, 0.62f, 0.25f, 1f);
    public static readonly Color Bad = new(0.95f, 0.35f, 0.30f, 1f);
    static readonly Color PanelFallback = new(0.09f, 0.075f, 0.06f, 0.96f);

    public static bool Ru => Progression.GameQuery.IsRussian();
    public static string T(string ru, string en) => Ru ? ru : en;

    public static TMP_FontAsset Font()
    {
        var f = FirstPersonController.Instance != null ? FirstPersonController.Instance.interactionText : null;
        return f != null ? f.font : null;
    }

    static Image ShrinePanel() => GrandmaShrineUI._instance?._dialogWindow?.GetComponent<Image>();
    static Image ShrineButton() => GrandmaShrineUI._instance?._actionButton?.image;

    public static GameObject Canvas(string name, int order)
    {
        var root = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    // Anchor + pivot at the same normalised point, then offset and size.
    public static RectTransform At(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
    }

    public static Image Panel(Transform parent, string name)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        var s = ShrinePanel();
        if (s != null && s.sprite != null) { img.sprite = s.sprite; img.type = s.type; img.color = s.color; }
        else img.color = PanelFallback;
        return img;
    }

    public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var t = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        var f = Font();
        if (f != null) t.font = f;
        t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
        t.raycastTarget = false;
        t.richText = true;
        TextFit.Block(t, size);
        return t;
    }

    public static Button Button(Transform parent, string label, float fontSize, Action onClick, out TextMeshProUGUI text)
    {
        var rt = Rect("Btn_" + label, parent);
        var img = rt.gameObject.AddComponent<Image>();
        var s = ShrineButton();
        if (s != null && s.sprite != null) { img.sprite = s.sprite; img.type = s.type; img.color = s.color; }
        else img.color = new Color(0.30f, 0.22f, 0.14f, 1f);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        text = Text(rt, "Label", label, fontSize, Ink, TextAlignmentOptions.Center);
        Stretch(text.rectTransform, 4, 2, 4, 2);
        b.onClick.AddListener((UnityAction)onClick);
        return b;
    }

    // A vertically scrolling text area: viewport with a mask and one auto-sized TMP inside.
    public static TextMeshProUGUI ScrollText(Transform parent, out ScrollRect scroll)
    {
        var view = Rect("Viewport", parent);
        view.gameObject.AddComponent<RectMask2D>();
        var bgHit = view.gameObject.AddComponent<Image>();
        bgHit.color = new Color(0, 0, 0, 0.01f); // receives scroll-wheel events
        scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var content = Rect("Content", view);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = new Vector2(10, 0); content.offsetMax = new Vector2(-10, 0);
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var text = content.gameObject.AddComponent<TextMeshProUGUI>();
        var f = Font();
        if (f != null) text.font = f;
        text.fontSize = 22; text.color = Ink; text.richText = true; text.raycastTarget = false;
        text.enableWordWrapping = true;
        scroll.content = content;
        scroll.viewport = view;
        return text;
    }

    public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    public static string Col(string s, Color c) => $"<color={Hex(c)}>{s}</color>";
}
