using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FellSellPlus.Inventory;

// Carry weight bar at the lower left, styled like a plain status bar. The end of the bar is always
// 100% of max carry weight; orange from 75%, red from 95%.
internal static class WeightBar
{
    static GameObject _root;
    static Image _fill;
    static TextMeshProUGUI _label;
    static bool _failed;

    static readonly Color Normal = new(0.85f, 0.78f, 0.55f, 0.95f);
    static readonly Color Warn = new(1f, 0.6f, 0.15f, 0.95f);
    static readonly Color Full = new(0.95f, 0.25f, 0.2f, 0.95f);

    public static void Tick()
    {
        if (_failed || !InventorySettings.WeightBar.Value) { Show(false); return; }
        var inv = InventoryManager.Instance;
        bool visible = inv != null && FirstPersonController.Instance != null && !FirstPersonController.IsPlayerBlocked
                       && ItemCounts.Scene() is ItemCounts.Town or ItemCounts.Dungeon or ItemCounts.Forest;
        if (!visible) { Show(false); return; }
        try
        {
            if (_root == null) Build();
            Show(true);
            float cur = inv.CurrentWeight, max = Mathf.Max(0.01f, inv.EffectiveMaxWeight);
            float t = Mathf.Clamp01(cur / max);
            _fill.rectTransform.anchorMax = new Vector2(t, 1f);
            _fill.color = t >= 0.95f ? Full : t >= 0.75f ? Warn : Normal;
            _label.text = $"{(Progression.GameQuery.IsRussian() ? "Вес" : "Weight")}: {cur:0.#} / {max:0.#}";
        }
        catch (Exception e) { _failed = true; Show(false); Plugin.L.LogError($"Weight bar disabled: {e.Message}"); }
    }

    static void Show(bool on) { if (_root != null && _root.activeSelf != on) _root.SetActive(on); }

    static void Build()
    {
        _root = new GameObject("FSP_WeightBar");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var bg = Rect("Bg", _root.transform);
        bg.anchorMin = bg.anchorMax = bg.pivot = Vector2.zero;
        bg.anchoredPosition = new Vector2(InventorySettings.WeightBarX.Value, InventorySettings.WeightBarY.Value);
        bg.sizeDelta = new Vector2(InventorySettings.WeightBarWidth.Value, 14f);
        bg.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        var fill = Rect("Fill", bg);
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = new Vector2(2f, 2f); fill.offsetMax = new Vector2(-2f, -2f);
        _fill = fill.gameObject.AddComponent<Image>();

        var lbl = Rect("Label", bg);
        lbl.anchorMin = Vector2.zero; lbl.anchorMax = Vector2.one;
        lbl.offsetMin = new Vector2(6f, 12f); lbl.offsetMax = new Vector2(0f, 30f);
        _label = lbl.gameObject.AddComponent<TextMeshProUGUI>();
        _label.fontSize = 16f;
        _label.alignment = TextAlignmentOptions.BottomLeft;
        TextFit.Line(_label, 16f);
        _label.color = new Color(1f, 1f, 1f, 0.9f);
        var font = GameFont();
        if (font != null) _label.font = font;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    public static TMP_FontAsset GameFont()
    {
        try
        {
            var any = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
            return any != null ? any.font : null;
        }
        catch { return null; }
    }
}
