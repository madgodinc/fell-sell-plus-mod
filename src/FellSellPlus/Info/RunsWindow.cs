using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FellSellPlus.Info;

// Run summary (shown on return to town) and the runs window (J): Overview, History, Segments,
// Combat and Loot tabs.
// Keyboard driven: 1/2 tabs, Up/Down pick a run, Enter opens its detail, Delete twice removes it.
internal static class RunsWindow
{
    static GameObject _root;
    static bool _builtRu;
    static TextMeshProUGUI _text, _footer;
    enum Mode { Closed, Summary, Window }
    static Mode _mode;
    static int _openedFrame;
    static RunRecord _summary;
    static int _tab;       // 0 overview, 1 history, 2 segments, 3 combat, 4 loot
    static int _sel, _scroll;
    static bool _detail;
    static float _deleteArmedUntil;
    const int PageRows = 18;

    public static void Tick()
    {
        if (!InfoSettings.Runs.Value) return;
        try
        {
            if (_mode == Mode.Closed)
            {
                if (InfoUi.Pressed(InfoUi.ParseKey(InfoSettings.RunsKey.Value, Key.J)) && CanOpen()) Open(Mode.Window);
                return;
            }
            ModalGuard.Touch();
            if (_root == null) { _mode = Mode.Closed; return; }
            if (!_root.activeSelf) { _mode = Mode.Closed; return; } // closed by the game's window manager
            if (Time.frameCount == _openedFrame) return;
            HandleInput();
        }
        catch (Exception e) { Plugin.L.LogWarning($"Runs window error: {e.Message}"); Close(); }
    }

    // Only when the player is in the world and no game menu holds the cursor.
    static bool CanOpen() => FirstPersonController.Instance != null && Cursor.lockState == CursorLockMode.Locked;

    public static void ShowSummary(RunRecord r)
    {
        _summary = r;
        if (_mode == Mode.Window) Close();
        Open(Mode.Summary);
    }

    static void Open(Mode m)
    {
        // Labels are written when the window is built: rebuild it after a language switch.
        if (_root != null && _builtRu != InfoUi.Ru) { UnityEngine.Object.Destroy(_root); _root = null; }
        if (_root == null) { Build(); _builtRu = InfoUi.Ru; }
        _mode = m;
        _openedFrame = Time.frameCount;
        _detail = false;
        _root.SetActive(true);
        GameWindows.Open(_root);
        Render();
    }

    static void Close()
    {
        if (_mode == Mode.Closed) return;
        _mode = Mode.Closed;
        GameWindows.Close(_root);
        if (_root != null) _root.SetActive(false);
    }

    static void Build()
    {
        _root = InfoUi.Canvas("FSP_Runs", 520);
        var dim = InfoUi.Rect("Dim", _root.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 3000));
        dim.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, 0.55f);
        var panel = InfoUi.Panel("Panel", _root.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1280, 860));
        _text = InfoUi.Text(panel, "Text", 20, TextAlignmentOptions.TopLeft, 36);
        var foot = InfoUi.Rect("Footer", panel, new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(1200, 40));
        _footer = InfoUi.Text(foot, "FooterText", 17, TextAlignmentOptions.Center, 0);
        _footer.color = InfoUi.Muted;
        _root.SetActive(false);
    }

    static void HandleInput()
    {
        if (_mode == Mode.Summary)
        {
            if (InfoUi.BackPressed() || InfoUi.Pressed(Key.Space) || InfoUi.Pressed(Key.Enter) || InfoUi.Pressed(Key.E)
                || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame))
                Close();
            return;
        }
        if (InfoUi.BackPressed()) { if (_detail) { _detail = false; Render(); } else Close(); return; }
        if (InfoUi.Pressed(InfoUi.ParseKey(InfoSettings.RunsKey.Value, Key.J))) { Close(); return; }
        var h = RunRecorder.History;
        bool dirty = false;
        if (InfoUi.Pressed(Key.Digit1)) { _tab = 0; _detail = false; dirty = true; }
        if (InfoUi.Pressed(Key.Digit2)) { _tab = 1; _detail = false; dirty = true; }
        if (InfoUi.Pressed(Key.Digit3)) { _tab = 2; _detail = false; dirty = true; }
        if (InfoUi.Pressed(Key.Digit4)) { _tab = 3; _detail = false; dirty = true; }
        if (InfoUi.Pressed(Key.Digit5)) { _tab = 4; _detail = false; dirty = true; }
        if (InfoUi.Pressed(Key.Digit6) && InfoSettings.LootOdds.Value) { _tab = 5; _detail = false; dirty = true; }
        if (_tab == 1 && h.Count > 0)
        {
            if (InfoUi.Pressed(Key.DownArrow)) { _sel = Math.Min(h.Count - 1, _sel + 1); dirty = true; }
            if (InfoUi.Pressed(Key.UpArrow)) { _sel = Math.Max(0, _sel - 1); dirty = true; }
            if (InfoUi.Pressed(Key.PageDown)) { _sel = Math.Min(h.Count - 1, _sel + PageRows); dirty = true; }
            if (InfoUi.Pressed(Key.PageUp)) { _sel = Math.Max(0, _sel - PageRows); dirty = true; }
            if (InfoUi.Pressed(Key.Enter)) { _detail = !_detail; dirty = true; }
            if (InfoUi.Pressed(Key.Delete))
            {
                if (Time.unscaledTime < _deleteArmedUntil) { RunRecorder.Delete(h[_sel]); _sel = Math.Max(0, Math.Min(_sel, RunRecorder.History.Count - 1)); _deleteArmedUntil = 0; _detail = false; }
                else _deleteArmedUntil = Time.unscaledTime + 3f;
                dirty = true;
            }
        }
        if (dirty) Render();
    }

    static void Render()
    {
        if (_mode == Mode.Summary)
        {
            _text.text = Detail(_summary, true);
            _footer.text = InfoUi.T("[Пробел / E / Esc] Закрыть", "[Space / E / Esc] Close");
            return;
        }
        string tabs = Tab(0, InfoUi.T("Обзор", "Overview")) + "   " + Tab(1, InfoUi.T("История", "History")) + "   "
                      + Tab(2, InfoUi.T("Отрезки", "Segments")) + "   " + Tab(3, InfoUi.T("Бой", "Combat")) + "   " + Tab(4, InfoUi.T("Добыча", "Loot"))
                      + (InfoSettings.LootOdds.Value ? "   " + Tab(5, InfoUi.T("Шансы", "Odds")) : "");
        string body = _tab switch
        {
            0 => Overview(),
            1 => _detail && RunRecorder.History.Count > 0 ? Detail(RunRecorder.History[_sel], false) : HistoryList(),
            2 => RunsTabs.Segments(),
            3 => RunsTabs.Combat(),
            5 => LootOdds.Render(),
            _ => RunsTabs.Loot(),
        };
        _text.text = $"<size=30><color={InfoUi.Hex(InfoUi.Gold)}>{InfoUi.T("Забеги", "Runs")}</color></size>    {tabs}\n\n{body}";
        _footer.text = _tab == 1
            ? (Time.unscaledTime < _deleteArmedUntil
                ? InfoUi.C(InfoUi.Bad, InfoUi.T("Нажмите Delete ещё раз, чтобы удалить забег", "Press Delete again to remove this run"))
                : InfoUi.T("[1-6] Вкладки  [↑↓] Выбор  [Enter] Подробно  [Delete] Удалить  [Esc] Закрыть",
                           "[1-6] Tabs  [↑↓] Select  [Enter] Details  [Delete] Remove  [Esc] Close"))
            : InfoUi.T("[1-6] Вкладки  [Esc] Закрыть", "[1-6] Tabs  [Esc] Close");
    }

    static string Tab(int i, string name) => i == _tab ? $"<u>{name}</u>" : InfoUi.C(InfoUi.Muted, name);

    static string AreaName(RunRecord r) => r.Area == "FOREST" ? InfoUi.T("Лес", "Forest")
        : (r.Tier > 0 ? $"{InfoUi.T("Подземелье", "Dungeon")}+{r.Tier}" : InfoUi.T("Подземелье", "Dungeon"));

    static string Outcome(RunRecord r) => r.Outcome switch
    {
        "died" => InfoUi.C(InfoUi.Bad, InfoUi.T("смерть", "died")),
        "quit" => InfoUi.C(InfoUi.Muted, InfoUi.T("выход", "quit")),
        _ => InfoUi.C(InfoUi.Good, InfoUi.T("вернулся", "returned")),
    };

    static string Overview()
    {
        var h = RunRecorder.History;
        if (h.Count == 0) return InfoUi.C(InfoUi.Muted, InfoUi.T("Пока нет записанных забегов.", "No runs recorded yet."));
        var sb = new StringBuilder();
        void L(string ru, string en, string v) => sb.Append($"{InfoUi.T(ru, en)}<pos=40%>{v}\n");
        L("Забегов", "Runs", h.Count.ToString());
        L("Смертей", "Deaths", h.Count(r => r.Outcome == "died").ToString());
        L("Глубже всего", "Deepest floor", h.Max(r => r.DeepestFloor).ToString());
        L("Время в забегах", "Time in runs", InfoUi.Time(h.Sum(r => r.Seconds)));
        L("Убито", "Kills", $"{h.Sum(r => r.TotalKills):N0} ({InfoUi.T("элиток", "elites")} {h.Sum(r => r.Elites)}, {InfoUi.T("боссов", "bosses")} {h.Sum(r => r.Bosses)})");
        L("Нанесено урона", "Damage dealt", $"{h.Sum(r => r.DamageDealt):N0} ({InfoUi.T("перебор", "overkill")} {h.Sum(r => r.Overkill):N0})");
        L("Получено урона", "Damage taken", h.Sum(r => r.TotalTaken).ToString("N0"));
        L("Сундуков", "Chests", $"{h.Sum(r => r.Chests)} + {h.Sum(r => r.BossChests)} {InfoUi.T("босс.", "boss")}");
        if (InfoSettings.ShowIdealValues.Value) L("Ценность лута", "Loot value", "~" + h.Sum(r => (long)r.LootValue).ToString("N0"));

        sb.Append($"\n<color={InfoUi.Hex(InfoUi.Gold)}>{InfoUi.T("Чаще всего убиваете", "Most killed")}</color>\n");
        var totals = new Dictionary<string, int>();
        foreach (var r in h) foreach (var k in r.Kills) RunRecorder.AddI(totals, k.Key, k.Value);
        foreach (var kv in totals.OrderByDescending(x => x.Value).Take(8))
            sb.Append($"{kv.Key}<pos=40%>{kv.Value:N0}\n");
        return sb.ToString();
    }

    static string HistoryList()
    {
        var h = RunRecorder.History;
        if (h.Count == 0) return InfoUi.C(InfoUi.Muted, InfoUi.T("Пока нет записанных забегов.", "No runs recorded yet."));
        _sel = Math.Clamp(_sel, 0, h.Count - 1);
        if (_sel < _scroll) _scroll = _sel;
        if (_sel >= _scroll + PageRows) _scroll = _sel - PageRows + 1;
        var sb = new StringBuilder();
        sb.Append(InfoUi.C(InfoUi.Muted, $"{InfoUi.T("Когда", "When")}<pos=17%>{InfoUi.T("Где", "Where")}<pos=33%>{InfoUi.T("Этажи", "Floors")}<pos=43%>{InfoUi.T("Время", "Time")}<pos=53%>{InfoUi.T("Убито", "Kills")}<pos=62%>{InfoUi.T("Урон", "Damage")}<pos=73%>{InfoUi.T("Добыча", "Loot")}<pos=84%>{InfoUi.T("Итог", "End")}")).Append('\n');
        for (int i = _scroll; i < Math.Min(h.Count, _scroll + PageRows); i++)
        {
            var r = h[i];
            string row = $"{r.Start:dd.MM HH:mm}<pos=17%>{AreaName(r)}<pos=33%>{Floors(r)}<pos=43%>{InfoUi.Time(r.Seconds)}<pos=53%>{r.TotalKills}<pos=62%>{r.DamageDealt:N0}<pos=73%>{(InfoSettings.ShowIdealValues.Value ? "~" + r.LootValue.ToString("N0") : r.Loot.Values.Sum().ToString())}<pos=84%>{Outcome(r)}";
            sb.Append(i == _sel ? $"<mark=#FFD06633>▶ {row}</mark>" : "  " + row).Append('\n');
        }
        return sb.ToString();
    }

    static string Floors(RunRecord r) => r.Area == "FOREST" ? "-" : (r.StartFloor == r.DeepestFloor ? $"{r.DeepestFloor}" : $"{r.StartFloor}-{r.DeepestFloor}");

    // Best earlier time for the same area, tier and floor range.
    static string PbNote(RunRecord r)
    {
        var same = RunRecorder.History.Where(x => !ReferenceEquals(x, r) && x.Start < r.Start && x.Area == r.Area && x.Tier == r.Tier
                                                  && x.StartFloor == r.StartFloor && x.DeepestFloor == r.DeepestFloor && x.Outcome == "returned").ToList();
        if (r.Outcome != "returned") return "";
        if (same.Count == 0) return InfoUi.C(InfoUi.Muted, InfoUi.T("  (первый такой забег)", "  (first run like this)"));
        double best = same.Min(x => x.Seconds), avg = same.Average(x => x.Seconds);
        string pb = r.Seconds < best ? InfoUi.C(InfoUi.Gold, "  PB!") : "";
        return $"{pb}  {InfoUi.C(InfoUi.Muted, $"{InfoUi.T("лучшее", "best")} {InfoUi.Time(best)}, {InfoUi.T("среднее", "avg")} {InfoUi.Time(avg)}")}";
    }

    static string Detail(RunRecord r, bool summary)
    {
        if (r == null) return "";
        var sb = new StringBuilder();
        string title = summary ? InfoUi.T("Итоги забега", "Run summary") : $"{r.Start:dd.MM.yyyy HH:mm}";
        sb.Append($"<size=30><color={InfoUi.Hex(InfoUi.Gold)}>{title}</color></size>    {AreaName(r)} · {Floors(r)} · {Outcome(r)}\n\n");
        sb.Append($"{InfoUi.T("Время", "Time")}<pos=22%>{InfoUi.Time(r.Seconds)}{PbNote(r)}\n");
        if (r.Floors.Count > 0)
            sb.Append($"{InfoUi.T("Этажи", "Floors")}<pos=22%>{string.Join("  ", r.Floors.Select(f => $"{f.Floor}: {InfoUi.Time(f.Seconds)}"))}\n");
        sb.Append($"{InfoUi.T("Убито", "Kills")}<pos=22%>{r.TotalKills}  {InfoUi.C(InfoUi.Muted, $"{InfoUi.T("элиток", "elites")} {r.Elites}, {InfoUi.T("боссов", "bosses")} {r.Bosses}")}\n");
        if (r.Kills.Count > 0)
            sb.Append($"<pos=22%>{InfoUi.C(InfoUi.Muted, string.Join(", ", r.Kills.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key} {k.Value}")))}\n");
        double critPct = r.Hits > 0 ? 100.0 * r.Crits / r.Hits : 0;
        sb.Append($"{InfoUi.T("Урон", "Damage")}<pos=22%>{r.DamageDealt:N0}  {InfoUi.C(InfoUi.Muted, $"{InfoUi.T("перебор", "overkill")} {r.Overkill:N0} · {InfoUi.T("ударов", "hits")} {r.Hits} · {InfoUi.T("крит", "crit")} {critPct:0}% · {InfoUi.T("макс.", "max")} {r.BiggestHit}")}\n");
        if (r.DamageDealtBySource.Count > 0)
            sb.Append($"<pos=22%>{InfoUi.C(InfoUi.Muted, string.Join(", ", r.DamageDealtBySource.OrderByDescending(k => k.Value).Take(5).Select(k => $"{k.Key} {k.Value:N0}")))}\n");
        sb.Append($"{InfoUi.T("Получено", "Taken")}<pos=22%>{r.TotalTaken:N0}");
        if (r.DamageTaken.Count > 0)
            sb.Append($"  {InfoUi.C(InfoUi.Muted, string.Join(", ", r.DamageTaken.OrderByDescending(k => k.Value).Take(5).Select(k => $"{k.Key} {k.Value:N0}")))}");
        sb.Append('\n');
        int items = r.Loot.Values.Sum();
        sb.Append($"{InfoUi.T("Добыча", "Loot")}<pos=22%>{items}");
        if (InfoSettings.ShowIdealValues.Value) sb.Append($"  ~{r.LootValue:N0} {InfoUi.T("з.", "g")}");
        if (r.LootByRarity.Count > 0)
            sb.Append("  " + string.Join("  ", r.LootByRarity.OrderBy(k => k.Key).Select(k => $"<color={InfoUi.RarityHex(int.Parse(k.Key))}>{k.Value}</color>")));
        sb.Append('\n');
        if (r.Loot.Count > 0)
            sb.Append($"<pos=22%>{InfoUi.C(InfoUi.Muted, string.Join(", ", r.Loot.OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key} x{k.Value}")))}\n");
        sb.Append($"{InfoUi.T("Сундуки", "Chests")}<pos=22%>{r.Chests}  {InfoUi.C(InfoUi.Muted, $"{InfoUi.T("босса", "boss")} {r.BossChests}")}\n");
        if (r.Prayers.Count > 0) sb.Append($"{InfoUi.T("Молитвы", "Prayers")}<pos=22%>{string.Join(", ", r.Prayers)}\n");
        return sb.ToString();
    }
}
