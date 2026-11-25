using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using Il2CppEmeraldAI;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Info;

internal sealed class FloorTime { public int Floor { get; set; } public double Seconds { get; set; } }

internal sealed class RunRecord
{
    public string Id { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Area { get; set; }          // DUNGEON or FOREST
    public int Tier { get; set; }             // Dungeon+ tier, 0 = normal
    public int StartFloor { get; set; }
    public int DeepestFloor { get; set; }
    public double Seconds { get; set; }
    public string Outcome { get; set; }       // returned / died / quit
    public List<FloorTime> Floors { get; set; } = new();
    public Dictionary<string, int> Kills { get; set; } = new();
    public int Elites { get; set; }
    public int Bosses { get; set; }
    public double DamageDealt { get; set; }
    public double Overkill { get; set; }
    public int Hits { get; set; }
    public int Crits { get; set; }
    public int BiggestHit { get; set; }
    public Dictionary<string, double> DamageDealtBySource { get; set; } = new();
    public Dictionary<string, double> DamageTaken { get; set; } = new();
    public Dictionary<string, int> LootByRarity { get; set; } = new();
    public Dictionary<string, int> Loot { get; set; } = new();
    public int LootValue { get; set; }
    public int Chests { get; set; }
    public int BossChests { get; set; }
    public List<string> Prayers { get; set; } = new();

    public int TotalKills => Kills.Values.Sum();
    public double TotalTaken => DamageTaken.Values.Sum();
}

// Records dungeon and forest runs. Run boundaries and floors are polled from the active scene and
// DungeonManager.currentFloor; combat, loot, chests and prayers come from the hooks below.
internal static class RunRecorder
{
    public static RunRecord Current;
    static readonly List<RunRecord> _history = new();
    static bool _loaded;
    static string _lastScene = "";
    static int _lastFloor = -1;
    static float _floorStart;
    static float _runStart;
    static bool _broken;

    static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus", "runs");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static IReadOnlyList<RunRecord> History { get { EnsureLoaded(); return _history; } }

    static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var f in Directory.GetFiles(Dir, "*.json"))
            {
                try { var r = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(f)); if (r != null) _history.Add(r); }
                catch (Exception e) { Plugin.L.LogWarning($"Skipping run file {Path.GetFileName(f)}: {e.Message}"); }
            }
            _history.Sort((a, b) => b.Start.CompareTo(a.Start));
        }
        catch (Exception e) { Plugin.L.LogWarning($"Run history load failed: {e.Message}"); }
    }

    public static void Delete(RunRecord r)
    {
        _history.Remove(r);
        try { File.Delete(Path.Combine(Dir, r.Id + ".json")); } catch { }
    }

    public static void Tick()
    {
        if (_broken || !InfoSettings.Runs.Value) return;
        try
        {
            string scene = SceneManager.GetActiveScene().name ?? "";
            if (scene != _lastScene)
            {
                bool wasRun = IsRunScene(_lastScene), isRun = IsRunScene(scene);
                if (wasRun && !isRun) Finish(scene == "MAIN" ? (PlayerStats.DiedInDungeon ? "died" : "returned") : "quit");
                if (isRun && Current == null && _lastScene == "MAIN") Begin(scene);
                if (isRun && Current != null && Current.Area != scene) { Finish("returned"); Begin(scene); }
                _lastScene = scene;
            }
            if (Current != null && scene == "DUNGEON") TrackFloor();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Run recorder disabled: {e.Message}");
            _broken = true;
        }
    }

    static bool IsRunScene(string s) => s == "DUNGEON" || s == "FOREST";

    static void Begin(string scene)
    {
        Current = new RunRecord
        {
            Id = DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            Start = DateTime.Now,
            Area = scene,
            Tier = SafeTier(),
        };
        _runStart = UnityEngine.Time.realtimeSinceStartup;
        _lastFloor = -1;
        if (scene == "DUNGEON") TrackFloor();
    }

    static int SafeTier()
    {
        try { return DungeonPlusManager.Instance != null ? DungeonPlusManager.Instance.ActiveTier : 0; } catch { return 0; }
    }

    static void TrackFloor()
    {
        var dm = DungeonManager.Instance;
        if (dm == null) return;
        int f = dm.currentFloor;
        if (f == _lastFloor) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        CloseFloor(now);
        if (Current.StartFloor == 0) Current.StartFloor = f;
        Current.DeepestFloor = Math.Max(Current.DeepestFloor, f);
        _lastFloor = f;
        _floorStart = now;
    }

    static void CloseFloor(float now)
    {
        if (_lastFloor < 0 || Current == null) return;
        Current.Floors.Add(new FloorTime { Floor = _lastFloor, Seconds = Math.Round(now - _floorStart, 1) });
    }

    static void Finish(string outcome)
    {
        if (Current == null) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        CloseFloor(now);
        var r = Current;
        Current = null;
        r.End = DateTime.Now;
        r.Seconds = Math.Round(now - _runStart, 1);
        r.Outcome = outcome;
        EnsureLoaded();
        // Runs where nothing happened (entered and left at once) are not worth keeping.
        if (r.Seconds < 10 && r.TotalKills == 0 && r.Loot.Count == 0) return;
        _history.Insert(0, r);
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, r.Id + ".json"), JsonSerializer.Serialize(r, Json));
        }
        catch (Exception e) { Plugin.L.LogWarning($"Run save failed: {e.Message}"); }
        if (InfoSettings.RunSummary.Value && outcome != "quit") RunsWindow.ShowSummary(r);
    }

    internal static string CleanName(string n)
    {
        if (string.IsNullOrEmpty(n)) return "?";
        int i = n.IndexOf("(Clone)", StringComparison.Ordinal);
        if (i >= 0) n = n.Substring(0, i);
        return n.Trim().TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ', '_', '(', ')');
    }

    static void Add<T>(Dictionary<string, T> d, string k, T v, Func<T, T, T> sum) =>
        d[k] = d.TryGetValue(k, out var old) ? sum(old, v) : v;
    internal static void AddI(Dictionary<string, int> d, string k, int v) => Add(d, k, v, (a, b) => a + b);
    internal static void AddD(Dictionary<string, double> d, string k, double v) => Add(d, k, v, (a, b) => a + b);

    // ---------- hooks ----------

    // Kill events: PlayerProgressionEventsListener.HandleEnemyKilled (0x18046BDB0), an event
    // handler invoked through a delegate (no direct call sites; the native detour still fires).
    [HarmonyPatch(typeof(PlayerProgressionEventsListener), "HandleEnemyKilled")]
    static class KillHook
    {
        static void Postfix(GameObject __0)
        {
            var r = Current;
            if (r == null || __0 == null) return;
            try
            {
                AddI(r.Kills, CleanName(__0.name), 1);
                if (__0.GetComponentInParent<EliteEnemyController>() != null) r.Elites++;
                var q = QuestStatsTracker.Instance;
                if (q != null && q.IsBoss(__0)) r.Bosses++;
            }
            catch { }
        }
    }

    // Damage to enemies: EmeraldHealth.DamageInternal (0x1806F1000), called by Damage and
    // DamageFromTrap. Args: amount, attacker, ragdoll force, critical, isTrapDamage.
    [HarmonyPatch(typeof(EmeraldHealth), "DamageInternal")]
    static class DealtHook
    {
        static void Prefix(EmeraldHealth __instance, int __0, Transform __1, bool __3, bool __4)
        {
            var r = Current;
            if (r == null || __instance == null || __0 <= 0) return;
            try
            {
                int hpLeft = __instance.CurrentHealth;
                if (hpLeft <= 0) return;
                string src = __4 ? "Trap" : WeaponName();
                r.DamageDealt += __0;
                r.Overkill += Math.Max(0, __0 - hpLeft);
                AddD(r.DamageDealtBySource, src, __0);
                if (!__4) { r.Hits++; if (__3) r.Crits++; r.BiggestHit = Math.Max(r.BiggestHit, __0); }
            }
            catch { }
        }

        static string WeaponName()
        {
            var w = InventoryManager.Instance?.rightHandSlot?.item;
            if (w == null) return "Unarmed";
            try { var n = w.GetDisplayName(); if (!string.IsNullOrEmpty(n)) return n; } catch { }
            return CleanName(w.name);
        }
    }

    // Damage to the player: private PlayerStats.TakeDamage(float, Transform, bool) (0x180490AF0),
    // reached from TakeDamage, TakeBombDamage and DebugTakeDamage. Measured as health actually lost.
    [HarmonyPatch(typeof(PlayerStats), "TakeDamage", new[] { typeof(float), typeof(Transform), typeof(bool) })]
    static class TakenHook
    {
        static void Prefix(PlayerStats __instance, out float __state) => __state = __instance != null ? __instance.CurrentHealth : 0f;

        static void Postfix(PlayerStats __instance, Transform __1, float __state)
        {
            var r = Current;
            if (r == null || __instance == null) return;
            try
            {
                float lost = __state - __instance.CurrentHealth;
                if (lost <= 0f) return;
                string src = __1 != null ? CleanName(__1.root.name) : "Other";
                AddD(r.DamageTaken, src, lost);
            }
            catch { }
        }
    }

    // Loot picked up: ItemPickup.PickUpItem (0x1807AACC0), called from Interact and the
    // auto-pickup coroutine.
    [HarmonyPatch(typeof(ItemPickup), "PickUpItem")]
    static class LootHook
    {
        static void Prefix(ItemPickup __instance)
        {
            var r = Current;
            if (r == null || __instance == null) return;
            try
            {
                var item = __instance.itemData;
                if (item == null) return;
                string name;
                try { name = item.GetDisplayName(); } catch { name = null; }
                if (string.IsNullOrEmpty(name)) name = CleanName(item.name);
                AddI(r.Loot, name, 1);
                AddI(r.LootByRarity, ((int)item.rarity).ToString(), 1);
                r.LootValue += Math.Max(0, item.idealPrice);
            }
            catch { }
        }
    }

    // Chests: PlayerProgressionEventsListener handlers (event delegates).
    [HarmonyPatch(typeof(PlayerProgressionEventsListener), "HandleDungeonChestOpened")]
    static class ChestHook { static void Postfix() { if (Current != null) Current.Chests++; } }

    [HarmonyPatch(typeof(PlayerProgressionEventsListener), "HandleForestChestOpened")]
    static class ForestChestHook { static void Postfix() { if (Current != null) Current.Chests++; } }

    [HarmonyPatch(typeof(PlayerProgressionEventsListener), "HandleBossChestOpened")]
    static class BossChestHook { static void Postfix() { if (Current != null) Current.BossChests++; } }

    // Prayers: PrayersManager.AddPrayer (0x180499BA0), called from PrayersUI.SelectBoon and
    // PrayersStatue.ChooseBoon.
    [HarmonyPatch(typeof(PrayersManager), nameof(PrayersManager.AddPrayer))]
    static class PrayerHook
    {
        static void Postfix(PrayerData __0)
        {
            var r = Current;
            if (r == null || __0 == null) return;
            string n;
            // Called through reflection: binding LocalizedString methods at compile time would pull in
            // UnityEngine.UIElementsModule as a reference.
            try
            {
                object ls = __0.localizedName;
                n = ls?.GetType().GetMethod("GetLocalizedString", Type.EmptyTypes)?.Invoke(ls, null) as string;
            }
            catch { n = null; }
            r.Prayers.Add($"{(string.IsNullOrEmpty(n) ? __0.name : n)} ({InfoUi.T("эт.", "fl.")} {Math.Max(0, _lastFloor)})");
        }
    }
}
