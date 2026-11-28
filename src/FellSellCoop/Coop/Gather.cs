using System.Text;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FellSellPlus.Coop;

// Gathering point: a dungeon floor starts only when every player of the run has loaded it. The host
// has the floor first; until each client that follows it stands on the same floor, everybody who is
// already there is held where he arrived and sees "Waiting for players 1/2". The host says in its
// state packets when the floor is open.
//
// Counted are the players who are in the dungeon, and for the first floor of a run also the clients
// who are on their way from the village (they follow the host in by themselves, DungeonSync). A
// partner who stays in the village does not hold up a run for long.
internal static class Gather
{
    const float GiveUp = 90f; // a client that never finishes loading does not lock the host in
    const float FromVillage = 40f; // how long the first floor waits for a client still in the village

    internal static bool Open { get; private set; } // host: the floor it stands on has started
    internal static bool HostOpen;                  // client: what the host last said
    internal static bool Holding => _holding;

    static int _openSeed, _gatherSeed;
    static float _since;
    static bool _holding;
    static string _lastScene = "";
    static float _enteredAt = -100f; // when this player came from the village into the dungeon
    static GameObject _root;
    static TextMeshProUGUI _title, _names;
    static readonly StringBuilder _missing = new();

    public static void Tick()
    {
        var fpc = FirstPersonController.Instance;
        string sceneNow = SceneManager.GetActiveScene().name;
        bool dungeon = sceneNow == "DUNGEON";
        // Loading scenes in between do not count: only a run that starts in the village waits for villagers.
        if (sceneNow != _lastScene && (sceneNow is "MAIN" or "DUNGEON" or "FOREST"))
        {
            if (dungeon && _lastScene == "MAIN") _enteredAt = Time.unscaledTime;
            _lastScene = sceneNow;
        }
        if (!CoopSettings.WaitForPlayers.Value || !CoopModule.Running || !dungeon || fpc == null)
        {
            if (!dungeon) { Open = false; _openSeed = 0; }
            else Open = CoopSettings.IsHost; // the setting is off: every floor is open at once
            Release(fpc);
            return;
        }
        if (CoopSettings.IsHost)
        {
            DungeonSync.Current(out int seed, out _);
            if (seed == 0) { Open = false; Release(fpc); return; } // still building: the loading screen is up
            if (_openSeed == seed) { Open = true; Release(fpc); return; }
            if (_gatherSeed != seed) { _gatherSeed = seed; _since = Time.unscaledTime; }
            Count(seed, false, out int ready, out int total);
            bool late = Time.unscaledTime - _since > GiveUp;
            if (ready >= total || late)
            {
                if (total > 1) Plugin.L.LogInfo(late ? $"Co-op: the floor starts without waiting any longer ({ready}/{total} players loaded it)." : $"Co-op: all {total} players have loaded the floor, it starts.");
                _openSeed = seed; Open = true;
                Release(fpc);
                return;
            }
            Open = false;
            Hold(fpc, ready, total);
            return;
        }
        // A client is held from the moment its copy of the host's floor is built until the host opens it.
        if (!DungeonSync.OnHostFloor || HostOpen) { Release(fpc); return; }
        Count(DungeonSync.HostSeed, true, out int r, out int t);
        Hold(fpc, r, t);
    }

    // Players in the run and how many of them stand on the floor with this seed.
    static void Count(int seed, bool client, out int ready, out int total)
    {
        ready = 1; total = 1; // this player: the host on its own floor, or a client that has built it
        _missing.Clear();
        foreach (var rem in CoopModule.Remotes)
        {
            bool coming = rem.Scene == "MAIN" && rem.Body.Joins && Time.unscaledTime - _enteredAt < FromVillage;
            if (!coming && (rem.Scene != "DUNGEON" || !(rem.IsHost || rem.Body.Follows))) continue;
            total++;
            if (!coming && (rem.IsHost || rem.Seed == seed)) ready++;
            else _missing.Append(_missing.Length > 0 ? ", " : "").Append(rem.Name);
        }
    }

    static void Hold(FirstPersonController fpc, int ready, int total)
    {
        if (!_holding)
        {
            _holding = true;
            Plugin.L.LogInfo($"Co-op: waiting for players ({ready}/{total}) before the floor starts.");
        }
        if (fpc.canMove) fpc.canMove = false;
        bool ru = Progression.GameQuery.IsRussian();
        if (_root == null) Build();
        _title.text = (ru ? "Ждём игроков " : "Waiting for players ") + ready + "/" + total;
        string names = _missing.Length == 0 ? "" : (ru ? "Загружает этаж: " : "Loading the floor: ") + _missing;
        string hard = DungeonSync.DifficultyLine(ru);
        _names.text = hard == null ? names : names.Length == 0 ? hard : names + "\n" + hard;
        if (!_root.activeSelf) _root.SetActive(true);
    }

    static void Release(FirstPersonController fpc)
    {
        if (!_holding) return;
        _holding = false;
        if (fpc != null && !Revive.Down) fpc.canMove = true;
        if (_root != null) _root.SetActive(false);
    }

    static void Build()
    {
        var gameFont = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
        _root = new GameObject("FellSellCoop_Gather");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var dim = new GameObject("Dim");
        var drt = dim.AddComponent<RectTransform>();
        drt.SetParent(_root.transform, false);
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = drt.offsetMax = Vector2.zero;
        var img = dim.AddComponent<Image>();
        img.color = new Color(0.02f, 0.015f, 0.01f, 0.62f);
        img.raycastTarget = false;

        _title = Line("Title", 52f, 40f, new Color(1f, 0.85f, 0.45f, 1f), gameFont);
        _names = Line("Names", 26f, -30f, new Color(0.93f, 0.88f, 0.78f, 0.9f), gameFont);
    }

    // A text box lower than about 1.6 of the font size draws nothing with this game's font.
    static TextMeshProUGUI Line(string name, float size, float y, Color color, TextMeshProUGUI gameFont)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(_root.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(1400f, size * 1.8f);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.overflowMode = TextOverflowModes.Overflow;
        t.color = color;
        t.raycastTarget = false;
        if (gameFont != null && gameFont.font != null) t.font = gameFont.font;
        return t;
    }
}
