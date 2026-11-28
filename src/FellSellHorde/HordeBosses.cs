using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FellSellHorde;

// The boss schedule (Mad, 2026-11-27): the first boss fight on wave 10, then one every BossEvery
// waves; FightsPerBiome (4) fights per biome in the dungeon's order (on 1.8.5 each biome has two
// bosses, so each comes twice, never twice in a row): Basic 10-25, Overgrown 30-45, Darkest 50-65.
// Then the pool opens (70) with Grandma in it: one boss per fight, one more every 30 waves.
// From wave 20 every boss brings its boss room's adds as a retinue.
internal static class HordeBosses
{
    internal sealed class Boss
    {
        public GameObject prefab;
        public List<GameObject> retinue = new();
        public bool grandma;
    }

    static List<Boss> _part1, _part2, _all;
    static readonly List<List<Boss>> _byBiome = new();
    static Boss _grandma;
    static readonly Dictionary<string, List<Boss>> Decks = new();
    static readonly System.Random Rng = new();
    static bool _grandmaMet;

    internal static void Reset()
    {
        _part1 = _part2 = _all = null;
        _byBiome.Clear();
        _grandma = null;
        Decks.Clear();
        LastDrawn.Clear();
        _grandmaMet = false;
    }

    // Boss fights per biome before the next biome takes over.
    internal const int FightsPerBiome = 4;
    internal const int PoolStepWaves = 30;

    // The wave the boss pool opens: 10 + 5 * fights of every biome (70 on game 1.8.5).
    internal static int PoolStart(CustomDungeonGenerator g)
    {
        Build(g);
        return 10 + Math.Max(1, HordeSettings.BossEvery.Value) * FightsPerBiome * Math.Max(1, _byBiome.Count);
    }

    // Biome phase of a wave for the regular enemies: the biome whose first boss fight is behind
    // (its enemies start the wave after), or -1 once the pool is open.
    internal static int BiomePhase(CustomDungeonGenerator g, int wave)
    {
        Build(g);
        if (wave > PoolStart(g)) return -1;
        int every = Math.Max(1, HordeSettings.BossEvery.Value);
        int phase = 0;
        for (int k = 1; k < _byBiome.Count; k++)
            if (wave > 10 + every * FightsPerBiome * k) phase = k;
        return phase;
    }

    internal static int BiomeCount(CustomDungeonGenerator g) { Build(g); return _byBiome.Count; }

    internal static List<Boss> ForWave(CustomDungeonGenerator g, int wave)
    {
        var none = new List<Boss>();
        int every = Math.Max(1, HordeSettings.BossEvery.Value);
        if (g == null || wave < 10 || (wave - 10) % every != 0) return none;
        Build(g);
        if (_all.Count == 0) return none;
        int n = (wave - 10) / every;
        // Biome by biome: FightsPerBiome boss fights each, that biome's bosses shuffled with no
        // boss twice in a row.
        if (n < FightsPerBiome * _byBiome.Count)
        {
            int bi = n / FightsPerBiome;
            return new List<Boss> { DrawNoRepeat("biome" + bi, _byBiome[bi]) };
        }
        // The pool (Grandma in it): one boss, one more every PoolStepWaves waves.
        var pool = _all.ToList();
        if (_grandma != null) pool.Add(_grandma);
        int count = 1 + Math.Max(0, wave - PoolStart(g)) / PoolStepWaves;
        var picked = new List<Boss>();
        for (int i = 0; i < count; i++)
        {
            Boss b; int guard = 0;
            do b = DrawNoRepeat("pool", pool); while (picked.Contains(b) && ++guard < 20 && picked.Count < pool.Count);
            picked.Add(b);
        }
        return picked;
    }

    static readonly Dictionary<string, Boss> LastDrawn = new();

    static Boss DrawNoRepeat(string deck, List<Boss> source)
    {
        var b = Draw(deck, source);
        if (source.Count > 1 && LastDrawn.TryGetValue(deck, out var last) && last == b)
        {
            var d = Decks[deck];
            if (d.Count > 0) { d.Add(b); b = d[0]; d.RemoveAt(0); }
        }
        LastDrawn[deck] = b;
        return b;
    }

    static List<Boss> PoolWithGrandma()
    {
        if (_grandma != null && _grandmaMet && !_all.Contains(_grandma)) { _all.Add(_grandma); Decks.Remove("all"); }
        return _all;
    }

    // Shuffled deck per tier: nobody repeats until everyone has been drawn.
    static Boss Draw(string deck, List<Boss> source)
    {
        if (!Decks.TryGetValue(deck, out var d) || d.Count == 0)
        {
            d = source.OrderBy(_ => Rng.Next()).ToList();
            Decks[deck] = d;
        }
        var b = d[0];
        d.RemoveAt(0);
        return b;
    }

    static void Build(CustomDungeonGenerator g)
    {
        if (_all != null) return;
        _all = new List<Boss>();
        var seen = new HashSet<IntPtr>();

        // Biomes in the order a normal run meets them.
        var biomes = new List<DungeonBiome>();
        var maps = g.biomeFloorMappings;
        var ordered = new List<(int floor, DungeonBiome b)>();
        for (int i = 0; maps != null && i < maps.Count; i++) if (maps[i]?.biome != null) ordered.Add((maps[i].minFloor, maps[i].biome));
        foreach (var (_, b) in ordered.OrderBy(x => x.floor)) if (!biomes.Any(x => x.Pointer == b.Pointer)) biomes.Add(b);
        for (int i = 0; g.availableBiomes != null && i < g.availableBiomes.Count; i++)
        {
            var b = g.availableBiomes[i];
            if (b != null && !biomes.Any(x => x.Pointer == b.Pointer)) biomes.Add(b);
        }

        foreach (var bi in biomes)
        {
            int before = _all.Count;
            for (int r = 0; bi.bossRooms != null && r < bi.bossRooms.Count; r++) AddRoom(bi.bossRooms[r]?.roomPrefab, seen, false);
            for (int r = 0; bi.bossRoomPrefabs != null && r < bi.bossRoomPrefabs.Count; r++) AddRoom(bi.bossRoomPrefabs[r], seen, false);
            if (_all.Count > before) _byBiome.Add(_all.Skip(before).ToList());
        }
        for (int r = 0; g.bossRoomPrefabs != null && r < g.bossRoomPrefabs.Count; r++) AddRoom(g.bossRoomPrefabs[r], seen, false);
        var gr = new List<Boss>();
        AddRoom(g.grandmaBossRoomPrefab, seen, true, gr);
        _grandma = gr.FirstOrDefault();

        _part1 = _all.Take(4).ToList();
        _part2 = _all.Skip(4).ToList();
        Plugin.L.Msg($"Horde bosses: part 1 [{string.Join(", ", _part1.Select(Describe))}], part 2 [{string.Join(", ", _part2.Select(Describe))}], grandma {(_grandma != null ? Describe(_grandma) : "none")}");
    }

    static string Describe(Boss b) => $"{b.prefab.name} (+{b.retinue.Count} retinue kinds)";

    static void AddRoom(GameObject room, HashSet<IntPtr> seen, bool grandma, List<Boss> into = null)
    {
        if (room == null) return;
        foreach (var sp in room.GetComponentsInChildren<BossRoomSpawner>(true))
        {
            var bosses = new List<GameObject>();
            var adds = new List<GameObject>();
            for (int e = 0; sp.enemiesToSpawn != null && e < sp.enemiesToSpawn.Count; e++)
            {
                var p = sp.enemiesToSpawn[e];
                if (p == null) continue;
                if (p.name.StartsWith("Boss") || grandma) bosses.Add(p); else adds.Add(p);
            }
            // Grandma's room: the first enemy is Grandma herself, the rest her retinue.
            if (grandma && bosses.Count > 1) { adds.AddRange(bosses.Skip(1)); bosses = bosses.Take(1).ToList(); }
            foreach (var p in bosses)
            {
                if (!seen.Add(p.Pointer)) continue;
                var b = new Boss { prefab = p, retinue = adds.Distinct().ToList(), grandma = grandma };
                (into ?? _all).Add(b);
            }
        }
    }
}
