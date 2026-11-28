using System;
using System.Text;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FellSellPlus.Coop;

// One or two lines at the top of the screen: where each partner is (village, forest, dungeon floor N)
// and, for a client held at the dungeon door, what it is waiting for.
internal static class CoopHud
{
    static GameObject _root;
    static TextMeshProUGUI _text;
    static float _next;
    static bool _failed;
    static readonly System.Collections.Generic.List<(string ru, string en, float until)> _notices = new();

    // A line that stays for a few seconds: a friend connected, a friend left.
    internal static void Notice(string ru, string en, float seconds = 10f)
    {
        _notices.Add((ru, en, Time.unscaledTime + seconds));
        _next = 0f;
    }

    // The session ended: nothing ticks this display any more.
    internal static void Hide()
    {
        _notices.Clear();
        if (_root != null) _root.SetActive(false);
    }

    public static void Tick()
    {
        if (_failed || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;
        try
        {
            // The co-op window says the same in its own words; the line would show through its veil.
            string label = FirstPersonController.Instance != null && !CoopMenu.IsOpen ? Describe() : null;
            if (label == null) { if (_root != null) _root.SetActive(false); return; }
            if (_root == null) Build();
            _text.text = label;
            // The horde's wave banner owns the top of the screen: the line goes under it.
            float y = HordeBridge.Active ? -215f : -120f;
            if (_text.rectTransform.anchoredPosition.y != y) _text.rectTransform.anchoredPosition = new Vector2(0f, y);
            _root.SetActive(true);
        }
        catch (Exception e) { _failed = true; Plugin.L.LogWarning($"Co-op status line disabled: {e.Message}"); }
    }

    static string Describe()
    {
        bool ru = Progression.GameQuery.IsRussian();
        var sb = new StringBuilder();
        bool alone = true;
        foreach (var _ in CoopModule.Remotes) { alone = false; break; }
        _notices.RemoveAll(n => Time.unscaledTime >= n.until);
        foreach (var n in _notices) sb.AppendLine(ru ? n.ru : n.en);
        if (alone)
        {
            string lobby = SteamLobby.Status(ru);
            if (lobby != null) sb.AppendLine(lobby);
        }
        if (DungeonSync.Waiting)
            sb.AppendLine(ru ? "Ждём, пока хост войдёт в подземелье" : "Waiting for the host to enter the dungeon");
        if (Revive.Down)
            sb.AppendLine(Revive.BossLocked
                ? (ru ? "Вы пали. Напарник поднимет вас, постояв рядом" : "You are down. Your partner can revive you by standing next to you")
                : (ru ? "Вы пали. Напарник поднимет вас у круга портала" : "You are down. Your partner can revive you at the portal circle"));
        else
        {
            var fallen = Revive.DownPartner();
            if (fallen != null && Revive.Reviving >= 0f)
                sb.AppendLine(ru ? $"Поднимаем {fallen.Name}: {Mathf.CeilToInt(Revive.Reviving)}" : $"Reviving {fallen.Name}: {Mathf.CeilToInt(Revive.Reviving)}");
            else if (fallen != null && Revive.BossLocked)
                sb.AppendLine(ru ? $"{fallen.Name} пал: постойте рядом {CoopSettings.ReviveSeconds.Value:0} с, чтобы поднять" : $"{fallen.Name} is down: stand next to him for {CoopSettings.ReviveSeconds.Value:0} s to revive");
            else if (fallen != null)
                sb.AppendLine(ru ? $"{fallen.Name} пал: встаньте у круга портала, чтобы поднять" : $"{fallen.Name} is down: stand by the portal circle to revive");
        }
        string follow = DungeonSync.FollowLine(ru);
        if (follow != null) sb.AppendLine(follow);
        string scene = SceneManager.GetActiveScene().name;
        int myFloor = DungeonManager.Instance != null ? DungeonManager.Instance.currentFloor : 0;
        foreach (var rem in CoopModule.Remotes)
        {
            string where = rem.Scene switch
            {
                "DUNGEON" => (ru ? "подземелье, этаж " : "dungeon, floor ") + rem.Floor,
                "FOREST" => ru ? "лес" : "forest",
                "MAIN" => ru ? "деревня" : "village",
                _ => ru ? "в меню" : "in a menu",
            };
            // Same scene and floor needs no line: the partner is right there.
            if (rem.Scene == scene && (scene != "DUNGEON" || rem.Floor == myFloor) && !DungeonSync.Waiting) continue;
            sb.AppendLine($"{rem.Name}: {where}");
        }
        return sb.Length == 0 ? null : sb.ToString().TrimEnd();
    }

    static void Build()
    {
        var gameFont = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
        _root = new GameObject("FellSellCoop_Status");
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
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -120f);
        rt.sizeDelta = new Vector2(900f, 90f);
        _text = go.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 24;
        _text.alignment = TextAlignmentOptions.Top;
        _text.color = new Color(1f, 0.85f, 0.45f, 0.95f);
        _text.raycastTarget = false;
        if (gameFont != null && gameFont.font != null) _text.font = gameFont.font;
    }
}
