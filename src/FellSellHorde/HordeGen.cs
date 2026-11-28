using System;
using HarmonyLib;
using UnityEngine;

namespace FellSellHorde;

// A horde run forces the first biome and its first boss room, which HordeBuild then makes the
// whole location: the arena hall and, behind it, the room with the exit portal and the two prayer
// statues (the side room). The player starts at the arena's entrance stairs.
// Every field changed here belongs to shared assets or the generator and is restored once
// generation finishes, so a normal run afterwards is untouched.
internal static class HordeGen
{
    internal static bool Armed;      // next dungeon entry becomes a horde run
    internal static bool Active;     // the current dungeon floor is a horde floor
    internal static bool Generating;
    internal static bool SawGenerating;
    internal static float BeganAt;

    static bool _saved;
    static bool _useRandomSeed;
    static int _randomSeed, _rooms, _maxRooms, _extraRooms, _bossInterval;
    static bool _bossRoom;
    static DungeonBiome _biome;
    internal static GameObject ArenaPrefab;

    internal static void Begin(CustomDungeonGenerator g)
    {
        if (!Armed && !Active) return;
        if (Armed) { Armed = false; Active = true; Plugin.L.Msg("Horde run starts: generating the arena floor."); }
        if (_saved) return;
        _saved = true;
        Generating = true;
        SawGenerating = false;
        BeganAt = UnityEngine.Time.time;
        _useRandomSeed = g.useRandomSeed; _randomSeed = g.randomSeed;
        _rooms = g.numberOfRooms; _maxRooms = g.maxNumberOfRooms; _bossRoom = g.generateBossRoom; _bossInterval = g.bossRoomInterval;
        var dm = DungeonManager.Instance;
        _extraRooms = dm != null ? dm.extraRoomsPerFloor : 0;
        // dev: a chosen seed for the floor that is thrown away (to bring back a layout that broke the build).
        if (HordeCoop.DevSeed != 0) { g.useRandomSeed = false; g.randomSeed = HordeCoop.DevSeed; Plugin.L.Msg($"Horde: dev seed {HordeCoop.DevSeed}."); }

        // The floor the generator builds is thrown away (HordeBuild replaces it); only the biome
        // is forced so the atmosphere and enemy lists are the first biome's.
    }

    // After the generator picked its biome: force the first biome and remember its first boss
    // room, which HordeBuild uses as the arena.
    internal static void ForceBiome(CustomDungeonGenerator g)
    {
        if (!Active || !_saved || _biome != null) return;
        DungeonBiome first = null;
        int best = int.MaxValue;
        var maps = g.biomeFloorMappings;
        for (int i = 0; maps != null && i < maps.Count; i++)
            if (maps[i]?.biome != null && maps[i].minFloor < best) { best = maps[i].minFloor; first = maps[i].biome; }
        if (first == null && g.availableBiomes != null && g.availableBiomes.Count > 0) first = g.availableBiomes[0];
        if (first == null) first = g._activeBiome;
        if (first == null) { Plugin.L.Warning("Horde: no biome found, keeping the generator's own choice."); return; }
        g._activeBiome = first;

        GameObject boss = null;
        var bosses = first.GetActiveBossRoomPrefabs();
        if (bosses != null && bosses.Count > 0) boss = bosses[0];
        if (boss == null && first.bossRooms != null && first.bossRooms.Count > 0) boss = first.bossRooms[0]?.roomPrefab;
        if (boss == null && g.bossRoomPrefabs != null && g.bossRoomPrefabs.Count > 0) boss = g.bossRoomPrefabs[0];
        ArenaPrefab = boss;

        _biome = first;
        Plugin.L.Msg($"Horde: biome '{first.biomeName}', arena prefab '{boss?.name ?? "none (start room kept)"}', end room '{first.endRoomPrefab?.name}'.");
    }

    internal static void Restore(CustomDungeonGenerator g)
    {
        if (!_saved) return;
        _saved = false;
        Generating = false;
        if (g != null)
        {
            // The seed the generator just drew stays: nothing here changed it, and the co-op mod
            // names the shared floor by it.
            g.useRandomSeed = _useRandomSeed;
            g.numberOfRooms = _rooms; g.maxNumberOfRooms = _maxRooms; g.generateBossRoom = _bossRoom;
            g.bossRoomInterval = _bossInterval;
        }
        _biome = null;
        var dm = DungeonManager.Instance;
        if (dm != null) dm.extraRoomsPerFloor = _extraRooms;
    }
}

[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.GenerateDungeonRoutine))]
internal static class GenRoutinePatch
{
    static void Prefix(CustomDungeonGenerator __instance) => HordeGen.Begin(__instance);
}

[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.GenerateDungeon))]
internal static class GenPatch
{
    static void Prefix(CustomDungeonGenerator __instance) => HordeGen.Begin(__instance);
}

// The boss room of a horde floor is always the biome's first boss room.
[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.GetNextBossRoomPrefab))]
internal static class ArenaBossPatch
{
    static void Postfix(ref GameObject __result)
    {
        if (!HordeGen.Generating || HordeGen.ArenaPrefab == null) return;
        __result = HordeGen.ArenaPrefab;
    }
}


[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.DetermineActiveBiome))]
internal static class BiomePatch
{
    static void Postfix(CustomDungeonGenerator __instance) => HordeGen.ForceBiome(__instance);
}

// No vanilla enemies on a horde floor: the director spawns everything.
[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.SpawnEnemiesInDungeon))]
internal static class NoDungeonEnemiesPatch { static bool Prefix() => !HordeGen.Active; }

[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.EnsureEnemiesExist))]
internal static class NoEnsureEnemiesPatch { static bool Prefix() => !HordeGen.Active; }

[HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.SpawnDefaultEnemiesAfterRestore))]
internal static class NoRestoreEnemiesPatch { static bool Prefix() => !HordeGen.Active; }

[HarmonyPatch(typeof(BossRoomSpawner), nameof(BossRoomSpawner.SpawnEnemiesNow))]
internal static class NoBossPatch { static bool Prefix() => !HordeGen.Active; }

// The horde floor is the whole run: no stairs to a next floor.
[HarmonyPatch(typeof(NextFloorTrigger), "OnTriggerEnter")]
internal static class NoNextFloorPatch { static bool Prefix() => !HordeGen.Active; }

// The entrance stairs in the start room lead back to the village: closed while a block is running.
[HarmonyPatch(typeof(AlternativeDungeonExit), "OnTriggerEnter")]
internal static class ExitGatePatch
{
    static bool Prefix() => !HordeGen.Active || HordeDirector.ExitAllowed();
}
