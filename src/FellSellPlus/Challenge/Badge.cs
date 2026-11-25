using System;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace FellSellPlus.Challenge;

// Small bottom-left label with the active challenge, e.g. "Challenge: Nightmare +2 (Dungeon+1)".
// Shown only in the world (player exists), hidden in menus and when ShowBadge is off.
internal static class Badge
{
    static GameObject _root;
    static TextMeshProUGUI _text;
    static float _next;
    static bool _failed;

    public static void Init() => MelonEvents.OnUpdate.Subscribe(Tick);

    static void Tick()
    {
        if (_failed || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        try
        {
            string label = ChallengeSettings.ShowBadge.Value ? ChallengeSettings.Describe() : null;
            bool show = label != null && FirstPersonController.Instance != null;
            if (!show) { if (_root != null) _root.SetActive(false); return; }
            if (_root == null) Build();
            int tier = DungeonPlusManager.Instance != null ? DungeonPlusManager.Instance.ActiveTier : 0;
            bool ru = Progression.GameQuery.IsRussian();
            _text.text = (ru ? "Испытание: " : "Challenge: ") + label + (tier > 0 ? $" (Dungeon+{tier})" : "");
            _root.SetActive(true);
        }
        catch (Exception e) { _failed = true; Plugin.L.LogWarning($"Challenge badge disabled: {e.Message}"); }
    }

    static void Build()
    {
        var gameFont = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
        _root = new GameObject("FellSellPlus_ChallengeBadge");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var go = new GameObject("Label");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_root.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(16f, 10f);
        rt.sizeDelta = new Vector2(700f, 30f);
        _text = go.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 18;
        _text.color = new Color(0.86f, 0.36f, 0.33f, 0.85f);
        _text.raycastTarget = false;
        TextFit.Line(_text, 18);
        if (gameFont != null && gameFont.font != null) _text.font = gameFont.font;
    }
}

internal static class ChallengeModule
{
    public static void Init(ConfigFile cfg)
    {
        ChallengeSettings.Bind(cfg);
        Badge.Init();
    }
}
