using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using S = FellSellPlus.Progression.ProgressionSettings;

namespace FellSellPlus.Progression;

internal enum Stat { MaxHealth, MaxStamina, MaxMana, HealthRegen, StaminaRegen, ManaRegen, Damage, MoveSpeed }

// Purchased levels for the current save slot. Stored inside the game's own save and written
// only when the game itself saves, so reloading an unsaved game rolls back gold and levels together.
internal static class ProgressionState
{
    public static readonly Stat[] All = (Stat[])Enum.GetValues(typeof(Stat));
    static readonly int[] Levels = new int[All.Length];
    static bool _newGamePending;

    static string SaveDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Art Games Studio SA", "Fell _ Sell");
    static string FileFor(int slot) => Path.Combine(SaveDir, $"FellSellPlus_SaveSlot_{slot}.json");

    public static int Level(Stat s) => Levels[(int)s];

    public static float PerLevel(Stat s) => s switch
    {
        Stat.MaxHealth => S.HealthPerLevel.Value,
        Stat.MaxStamina => S.StaminaPerLevel.Value,
        Stat.MaxMana => S.ManaPerLevel.Value,
        Stat.HealthRegen => S.HealthRegenPerLevel.Value,
        Stat.StaminaRegen => S.StaminaRegenPerLevel.Value,
        Stat.ManaRegen => S.ManaRegenPerLevel.Value,
        Stat.Damage => S.DamagePercentPerLevel.Value,
        Stat.MoveSpeed => S.SpeedPercentPerLevel.Value,
        _ => 0f,
    };

    // Flat amount for pools/regen, percent for damage/speed.
    public static float Bonus(Stat s)
    {
        float b = Level(s) * PerLevel(s);
        return s == Stat.MoveSpeed ? Math.Min(b, S.SpeedPercentCap.Value) : b;
    }

    public static float DamageMultiplier => 1f + Bonus(Stat.Damage) / 100f;
    public static float SpeedMultiplier => 1f + Bonus(Stat.MoveSpeed) / 100f;

    // Prestige tiers, e.g. "0:5, 0.5:10, 1:15": the highest tier reached gives the cap.
    public static List<(float prestige, int cap)> Tiers()
    {
        var list = new List<(float, int)>();
        foreach (var part in S.PrestigeTiers.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && float.TryParse(kv[0].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pr)
                && int.TryParse(kv[1], out var c)) list.Add((pr, c));
        }
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    public static int TierCap()
    {
        float p = GameQuery.Prestige(); int cap = 0;
        foreach (var t in Tiers()) if (p + 1e-4f >= t.prestige) cap = t.cap;
        return cap;
    }

    // The next tier above the current prestige, if any.
    public static (float prestige, int cap)? NextTier()
    {
        float p = GameQuery.Prestige();
        foreach (var t in Tiers()) if (p + 1e-4f < t.prestige) return t;
        return null;
    }

    public static int Cap(Stat s)
    {
        int cap = TierCap() + S.LevelCapPerBoss.Value * GameQuery.BossesKilled();
        if (s == Stat.MoveSpeed && PerLevel(s) > 0f)
            cap = Math.Min(cap, (int)Math.Ceiling(S.SpeedPercentCap.Value / PerLevel(s)));
        return cap;
    }

    // Default ladder: 50, 75, 100, 150, 300, 500, 750, 1000, then +500 per level (cfg).
    public static int NextCost(Stat s)
    {
        var steps = new List<int>();
        foreach (var p in S.CostSchedule.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(p, out var v) && v > 0) steps.Add(v);
        if (steps.Count == 0) steps.Add(50);
        int lvl = Level(s); // cost of buying level lvl+1
        if (lvl < steps.Count) return steps[lvl];
        long cost = steps[^1] + (long)S.CostStepAfter.Value * (lvl - steps.Count + 1);
        return (int)Math.Min(int.MaxValue, cost);
    }

    public static bool Unlocked => TierCap() > 0;

    public static bool TryBuy(Stat s)
    {
        if (!Unlocked || Level(s) >= Cap(s)) return false;
        if (!GameQuery.SpendGold(NextCost(s))) return false;
        Levels[(int)s]++;
        GameQuery.RefreshPlayerStats();
        return true;
    }

    public static void OnNewGame()
    {
        Array.Clear(Levels, 0, Levels.Length);
        _newGamePending = true;
    }

    // Levels live inside the game's own save file (ES3.Save<int>, the same call the game uses for
    // its XP ints), written into whichever file the game is saving right now: main slot, autosave or
    // backup. Loading any older save therefore rolls levels back together with the gold spent.
    const string KeyPrefix = "FellSellPlus_Progression_";

    public static void Save(string file)
    {
        if (string.IsNullOrEmpty(file)) return;
        _newGamePending = false;
        try
        {
            foreach (var s in All) ES3.Save<int>(KeyPrefix + s, Levels[(int)s], file);
        }
        catch (Exception e) { Plugin.L.LogError($"Progression save failed: {e.Message}"); }
    }

    public static void Load(string file)
    {
        if (_newGamePending) return; // fresh game in a reused slot: keep zeros until the first save
        Array.Clear(Levels, 0, Levels.Length);
        if (string.IsNullOrEmpty(file)) return;
        try
        {
            if (ES3.KeyExists(KeyPrefix + Stat.MaxHealth, file))
            {
                foreach (var s in All) Levels[(int)s] = Math.Max(0, ES3.Load<int>(KeyPrefix + s, file, 0));
                return;
            }
            // Saves made before this version kept levels in a side file per slot: migrate once.
            int slot = GameQuery.CurrentSlot();
            var path = slot >= 0 ? FileFor(slot) : null;
            if (path == null || !File.Exists(path)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
            foreach (var s in All)
                if (data != null && data.TryGetValue(s.ToString(), out var v)) Levels[(int)s] = Math.Max(0, v);
        }
        catch (Exception e) { Plugin.L.LogError($"Progression load failed: {e.Message}"); }
    }

    public static void DeleteSlot(int slot)
    {
        try { File.Delete(FileFor(slot)); } catch { /* nothing to delete */ }
    }
}
