using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using MelonLoader.Utils;

namespace FellSellPlus.Info;

// Dungeon chest odds per biome and floor, measured with the game's own roll.
// DungeonChest.GenerateLoot (0x180513FB0, caller DungeonChest.Initialize) rolls each of maxSlots slots:
// empty with weight biome.nothingWeight against the chest's baseC+baseU+baseR+baseE+baseL
// (0x1805144F6..0x18051455A), otherwise activeBiome.RollItemFromBiome(dungeonLevel, rnd, prayerBonus,
// dungeonBonus, false) (0x180622FB0). The arguments the game passes during a chest roll are captured,
// then RollItemFromBiome is sampled with them to get the rarity split. Recipe chance:
// (recipeBaseChance + recipeBonusChance * level) * (1 + recipe bonus / 100) (0x180515602..0x180515647);
// the furniture/gear part of the recipe bonus is not included (marked ≈).
internal static class LootOdds
{
    public sealed class Table
    {
        public string Biome { get; set; } = "";
        public int Tier { get; set; }
        public int Floor { get; set; }
        public int Level { get; set; }
        public int SlotsMin { get; set; }
        public int SlotsMax { get; set; }
        public double ItemChance { get; set; }       // a slot holds an item
        public double[] Rarity { get; set; } = new double[6]; // share of items by ItemRarity
        public double RecipeChance { get; set; }     // per chest, ≈
        public float PrayerBonus { get; set; }
        public float DungeonBonus { get; set; }
        public int Samples { get; set; }
        public DateTime Updated { get; set; }
    }

    const int Samples = 3000;
    static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus", "loot_tables.json");
    static Dictionary<string, Table> _tables;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // Captured during the game's own chest roll.
    internal static DungeonChest RollingChest;
    internal static bool Sampling;
    static DungeonBiome _biome;
    static int _level, _slotsMin = int.MaxValue, _slotsMax;
    static float _prayer, _dungeon, _nothing, _baseSum, _recipe;
    static string _pendingKey;

    public static Dictionary<string, Table> Tables
    {
        get
        {
            if (_tables != null) return _tables;
            try { _tables = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, Table>>(File.ReadAllText(FilePath)) : null; }
            catch (Exception e) { Plugin.L.LogWarning($"loot_tables.json unreadable: {e.Message}"); }
            return _tables ??= new();
        }
    }

    internal static void Capture(DungeonBiome biome, int level, float prayer, float dungeon)
    {
        var c = RollingChest;
        if (c == null || biome == null) return;
        _biome = biome; _level = level; _prayer = prayer; _dungeon = dungeon;
        _nothing = biome.nothingWeight;
        _baseSum = c.baseC + c.baseU + c.baseR + c.baseE + c.baseL;
        _slotsMin = Math.Min(_slotsMin, c.maxSlots); _slotsMax = Math.Max(_slotsMax, c.maxSlots);
        _recipe = (c.recipeBaseChance + c.recipeBonusChance * level) * (1f + DungeonPlusManager.GetRecipeDropChanceBonus(-1) / 100f);
        int floor = DungeonManager.Instance != null ? DungeonManager.Instance.currentFloor : 0;
        int tier = 0;
        try { tier = DungeonPlusManager.Instance != null ? DungeonPlusManager.Instance.ActiveTier : 0; } catch { }
        _pendingKey = $"{biome.biomeName}|{tier}|{floor}";
    }

    // Runs from InfoModule.Tick in the dungeon: samples once per captured floor.
    public static void Tick()
    {
        if (!InfoSettings.LootOdds.Value || _pendingKey == null || _biome == null) return;
        if (Inventory.ItemCounts.Scene() != "DUNGEON") { _pendingKey = null; return; }
        var key = _pendingKey; _pendingKey = null;
        var parts = key.Split('|');
        var t = new Table
        {
            Biome = parts[0], Tier = int.Parse(parts[1]), Floor = int.Parse(parts[2]), Level = _level,
            SlotsMin = _slotsMin == int.MaxValue ? 0 : _slotsMin, SlotsMax = _slotsMax,
            ItemChance = _baseSum + _nothing > 0 ? _baseSum / (_baseSum + _nothing) : 0,
            RecipeChance = _recipe, PrayerBonus = _prayer, DungeonBonus = _dungeon, Updated = DateTime.Now,
        };
        var counts = new int[6];
        int got = 0;
        Sampling = true;
        try
        {
            var rnd = new Il2CppSystem.Random(Environment.TickCount);
            for (int i = 0; i < Samples; i++)
            {
                var item = _biome.RollItemFromBiome(_level, rnd, _prayer, _dungeon, false);
                if (item == null) continue;
                int r = (int)item.rarity;
                if (r >= 0 && r < 6) { counts[r]++; got++; }
            }
        }
        finally { Sampling = false; }
        if (got == 0) return;
        for (int i = 0; i < 6; i++) t.Rarity[i] = (double)counts[i] / got;
        t.Samples = got;
        Tables[key] = t;
        _slotsMin = int.MaxValue; _slotsMax = 0;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Tables, Json));
        }
        catch (Exception e) { Plugin.L.LogWarning($"loot_tables.json not saved: {e.Message}"); }
    }

    // Runs window tab.
    public static string Render()
    {
        var tables = Tables;
        if (tables.Count == 0)
            return InfoUi.C(InfoUi.Muted, InfoUi.T("Шансы появятся после первого этажа подземелья: таблицы игра грузит только там.",
                                                   "Odds appear after your first dungeon floor: the game only loads the tables there."));
        var sb = new StringBuilder();
        sb.Append(InfoUi.C(InfoUi.Muted, InfoUi.T("Сундуки подземелья. Шансы при бонусе выпадения на момент записи этажа.",
                                                  "Dungeon chests. Odds at the drop bonus you had when the floor was recorded."))).Append('\n');
        string[] rn = { InfoUi.T("Об", "C"), InfoUi.T("Необ", "U"), InfoUi.T("Ред", "R"), InfoUi.T("Эп", "E"), InfoUi.T("Лег", "L"), InfoUi.T("Бог", "B") };
        sb.Append(InfoUi.C(InfoUi.Muted, $"{InfoUi.T("Этаж", "Floor")}<pos=10%>{InfoUi.T("Слоты", "Slots")}<pos=19%>{InfoUi.T("Предмет", "Item")}"
            + string.Concat(Enumerable.Range(0, 6).Select(i => $"<pos={30 + i * 9}%>{rn[i]}")) + $"<pos={30 + 6 * 9}%>{InfoUi.T("Рецепт≈", "Recipe≈")}")).Append('\n');
        foreach (var g in tables.Values.GroupBy(t => (t.Biome, t.Tier)).OrderBy(g => g.Key.Tier).ThenBy(g => g.Min(t => t.Floor)))
        {
            sb.Append('\n').Append($"<color={InfoUi.Hex(InfoUi.Gold)}>{g.Key.Biome}{(g.Key.Tier > 0 ? $"  +{g.Key.Tier}" : "")}</color>\n");
            foreach (var t in g.OrderBy(t => t.Floor))
            {
                string slots = t.SlotsMin == t.SlotsMax ? $"{t.SlotsMax}" : $"{t.SlotsMin}-{t.SlotsMax}";
                sb.Append($"{t.Floor}<pos=10%>{slots}<pos=19%>{t.ItemChance:P0}");
                for (int i = 0; i < 6; i++)
                    sb.Append($"<pos={30 + i * 9}%>").Append(t.Rarity[i] > 0 ? $"<color={InfoUi.RarityHex(i)}>{t.Rarity[i]:P1}</color>" : InfoUi.C(InfoUi.Muted, "-"));
                double rc = t.RecipeChance > 1 ? t.RecipeChance / 100.0 : t.RecipeChance;
                sb.Append($"<pos={30 + 6 * 9}%>{Math.Min(1, rc):P0}\n");
            }
        }
        return sb.ToString();
    }
}

// Marks the chest being rolled so RollItemFromBiome calls can be attributed to chests.
[HarmonyPatch(typeof(DungeonChest), "GenerateLoot")]
internal static class LootOddsChestPatch
{
    static void Prefix(DungeonChest __instance) { if (InfoSettings.LootOdds.Value) LootOdds.RollingChest = __instance; }
    static void Postfix() => LootOdds.RollingChest = null;
}

// RollItemFromBiome (0x180622FB0) is called directly from GenerateLoot (0x18051458C, 0x180514684).
[HarmonyPatch(typeof(DungeonBiome), nameof(DungeonBiome.RollItemFromBiome))]
internal static class LootOddsCapturePatch
{
    static void Prefix(DungeonBiome __instance, int dungeonLevel, float bonusPrayers, float bonusDungeon)
    {
        if (LootOdds.Sampling || LootOdds.RollingChest == null) return;
        try { LootOdds.Capture(__instance, dungeonLevel, bonusPrayers, bonusDungeon); }
        catch (Exception e) { Plugin.L.LogWarning($"Loot odds capture: {e.Message}"); }
    }
}
