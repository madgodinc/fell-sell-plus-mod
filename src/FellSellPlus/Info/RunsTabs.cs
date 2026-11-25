using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FellSellPlus.Info;

// Extra tabs of the runs window, built from the recorded run files only.
internal static class RunsTabs
{
    static string H(string ru, string en) => $"<color={InfoUi.Hex(InfoUi.Gold)}>{InfoUi.T(ru, en)}</color>\n";
    static string None() => InfoUi.C(InfoUi.Muted, InfoUi.T("Пока нет записанных забегов.", "No runs recorded yet."));

    static string Area(RunRecord r) => r.Area == "FOREST" ? InfoUi.T("Лес", "Forest")
        : (r.Tier > 0 ? $"{InfoUi.T("Подземелье", "Dungeon")}+{r.Tier}" : InfoUi.T("Подземелье", "Dungeon"));

    // Completed runs grouped by area, tier and floor range; plus every floor's own times.
    public static string Segments()
    {
        var h = RunRecorder.History;
        if (h.Count == 0) return None();
        var sb = new StringBuilder();
        sb.Append(H("Забеги одной длины (только завершённые)", "Runs of the same span (returned only)"));
        sb.Append(InfoUi.C(InfoUi.Muted, $"{InfoUi.T("Где", "Where")}<pos=22%>{InfoUi.T("Этажи", "Floors")}<pos=34%>{InfoUi.T("Раз", "Runs")}<pos=44%>{InfoUi.T("Лучшее", "Best")}<pos=58%>{InfoUi.T("Среднее", "Average")}<pos=72%>{InfoUi.T("Последнее", "Last")}")).Append('\n');
        foreach (var g in h.Where(r => r.Outcome == "returned")
                           .GroupBy(r => (r.Area, r.Tier, r.StartFloor, r.DeepestFloor))
                           .OrderBy(g => g.Key.Area).ThenBy(g => g.Key.Tier).ThenBy(g => g.Key.StartFloor).ThenBy(g => g.Key.DeepestFloor))
        {
            var runs = g.OrderBy(r => r.Start).ToList();
            var r0 = runs[0];
            string floors = r0.Area == "FOREST" ? "-" : (g.Key.StartFloor == g.Key.DeepestFloor ? $"{g.Key.StartFloor}" : $"{g.Key.StartFloor}-{g.Key.DeepestFloor}");
            sb.Append($"{Area(r0)}<pos=22%>{floors}<pos=34%>{runs.Count}<pos=44%>{InfoUi.Time(runs.Min(r => r.Seconds))}<pos=58%>{InfoUi.Time(runs.Average(r => r.Seconds))}<pos=72%>{InfoUi.Time(runs[^1].Seconds)}\n");
        }

        var floorsAll = h.Where(r => r.Area == "DUNGEON").SelectMany(r => r.Floors.Select(f => (r.Tier, f.Floor, f.Seconds)))
                         .Where(x => x.Seconds > 1).GroupBy(x => (x.Tier, x.Floor)).OrderBy(g => g.Key.Tier).ThenBy(g => g.Key.Floor).ToList();
        if (floorsAll.Count > 0)
        {
            sb.Append('\n').Append(H("Этажи подземелья", "Dungeon floors"));
            sb.Append(InfoUi.C(InfoUi.Muted, $"{InfoUi.T("Этаж", "Floor")}<pos=22%>{InfoUi.T("Раз", "Times")}<pos=34%>{InfoUi.T("Лучшее", "Best")}<pos=48%>{InfoUi.T("Среднее", "Average")}")).Append('\n');
            foreach (var g in floorsAll)
                sb.Append($"{(g.Key.Tier > 0 ? $"+{g.Key.Tier} " : "")}{g.Key.Floor}<pos=22%>{g.Count()}<pos=34%>{InfoUi.Time(g.Min(x => x.Seconds))}<pos=48%>{InfoUi.Time(g.Average(x => x.Seconds))}\n");
        }
        return sb.ToString();
    }

    // Damage dealt and taken by source, kills by enemy, over all runs and over the last one.
    public static string Combat()
    {
        var h = RunRecorder.History;
        if (h.Count == 0) return None();
        var sb = new StringBuilder();
        long hits = h.Sum(r => (long)r.Hits), crits = h.Sum(r => (long)r.Crits);
        sb.Append(H("Все забеги", "All runs"));
        sb.Append($"{InfoUi.T("Урон", "Damage")}<pos=22%>{h.Sum(r => r.DamageDealt):N0}  {InfoUi.C(InfoUi.Muted, $"{InfoUi.T("перебор", "overkill")} {h.Sum(r => r.Overkill):N0} · {InfoUi.T("ударов", "hits")} {hits:N0} · {InfoUi.T("крит", "crit")} {(hits > 0 ? 100.0 * crits / hits : 0):0}% · {InfoUi.T("макс.", "max")} {h.Max(r => r.BiggestHit)}")}\n");
        Table(sb, InfoUi.T("Урон по источникам", "Damage by source"), Sum(h.Select(r => r.DamageDealtBySource)), 10);
        Table(sb, InfoUi.T("Полученный урон", "Damage Received"), Sum(h.Select(r => r.DamageTaken)), 10);
        var kills = new Dictionary<string, double>();
        foreach (var r in h) foreach (var k in r.Kills) kills[k.Key] = kills.GetValueOrDefault(k.Key) + k.Value;
        Table(sb, InfoUi.T("Убито по типам", "Kills by enemy"), kills, 12);

        var last = h[0];
        sb.Append('\n').Append(H($"Последний забег ({last.Start:dd.MM HH:mm})", $"Last run ({last.Start:dd.MM HH:mm})"));
        Table(sb, InfoUi.T("Урон по источникам", "Damage by source"), last.DamageDealtBySource, 6);
        Table(sb, InfoUi.T("Полученный урон", "Damage Received"), last.DamageTaken, 6);
        return sb.ToString();
    }

    // Loot found per area and tier: items by rarity, chests, and the most found items.
    public static string Loot()
    {
        var h = RunRecorder.History;
        if (h.Count == 0) return None();
        var sb = new StringBuilder();
        foreach (var g in h.GroupBy(r => (r.Area, r.Tier)).OrderBy(g => g.Key.Area).ThenBy(g => g.Key.Tier))
        {
            var runs = g.ToList();
            sb.Append(H($"{Area(runs[0])} · {runs.Count} {InfoUi.T("забегов", "runs")}", $"{Area(runs[0])} · {runs.Count} runs"));
            int items = runs.Sum(r => r.Loot.Values.Sum()), chests = runs.Sum(r => r.Chests), boss = runs.Sum(r => r.BossChests);
            sb.Append($"{InfoUi.T("Предметов", "Items")}<pos=22%>{items}  {InfoUi.C(InfoUi.Muted, $"{InfoUi.T("в среднем за забег", "per run")} {(double)items / runs.Count:0.#}")}\n");
            sb.Append($"{InfoUi.T("Сундуков", "Chests")}<pos=22%>{chests} + {boss} {InfoUi.T("босса", "boss")}\n");
            var rar = new Dictionary<string, int>();
            foreach (var r in runs) foreach (var kv in r.LootByRarity) rar[kv.Key] = rar.GetValueOrDefault(kv.Key) + kv.Value;
            int tot = rar.Values.Sum();
            if (tot > 0)
                sb.Append($"{InfoUi.T("Редкость", "Rarity")}<pos=22%>" + string.Join("  ", rar.OrderBy(k => k.Key)
                    .Select(k => int.TryParse(k.Key, out var ri) ? $"<color={InfoUi.RarityHex(ri)}>{100.0 * k.Value / tot:0}%</color>" : $"{k.Key} {k.Value}")) + "\n");
            var top = new Dictionary<string, double>();
            foreach (var r in runs) foreach (var kv in r.Loot) top[kv.Key] = top.GetValueOrDefault(kv.Key) + kv.Value;
            sb.Append($"<pos=22%>{InfoUi.C(InfoUi.Muted, string.Join(", ", top.OrderByDescending(k => k.Value).Take(10).Select(k => $"{k.Key} x{k.Value:0}")))}\n\n");
        }
        return sb.ToString();
    }

    static Dictionary<string, double> Sum(IEnumerable<Dictionary<string, double>> ds)
    {
        var d = new Dictionary<string, double>();
        foreach (var x in ds) foreach (var kv in x) d[kv.Key] = d.GetValueOrDefault(kv.Key) + kv.Value;
        return d;
    }

    static void Table(StringBuilder sb, string title, Dictionary<string, double> d, int top)
    {
        if (d == null || d.Count == 0) return;
        double total = d.Values.Sum();
        sb.Append(InfoUi.C(InfoUi.Muted, title)).Append('\n');
        foreach (var kv in d.OrderByDescending(k => k.Value).Take(top))
            sb.Append($"  {kv.Key}<pos=40%>{kv.Value:N0}<pos=55%>{InfoUi.C(InfoUi.Muted, total > 0 ? $"{100 * kv.Value / total:0}%" : "")}\n");
    }
}
