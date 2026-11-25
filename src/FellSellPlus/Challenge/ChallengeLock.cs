using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HarmonyLib;
using MelonLoader.Utils;

namespace FellSellPlus.Challenge;

// Optional per-save "only harder" lock. The first time a save is loaded (or saved) with the lock on,
// every challenge value is written to UserData/FellSellPlus/challenge_slot<N>.json. From then on a
// value is never easier than the recorded one, and the record follows any harder change.
internal static class ChallengeLock
{
    static Dictionary<string, float> _locked;   // null = no lock for the current save
    static int _slot = -1;

    static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus");
    static string FileFor(int slot) => Path.Combine(Dir, $"challenge_slot{slot}.json");
    static string Id(Param p) => p.Section + "." + p.Key;

    public static float Apply(Param p, float value)
    {
        if (_locked == null || !_locked.TryGetValue(Id(p), out var rec)) return value;
        return p.HarderOf(value, rec);
    }

    public static void OnSaveTouched()
    {
        if (!ChallengeSettings.OnlyHarderLock.Value) { _locked = null; return; }
        int slot = SaveManager.Instance != null ? SaveManager.Instance.CurrentSlot : -1;
        if (slot < 0) return;
        try
        {
            if (slot != _slot || _locked == null)
            {
                _slot = slot;
                var f = FileFor(slot);
                _locked = File.Exists(f)
                    ? JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(f)) ?? new()
                    : new();
            }
            bool changed = false;
            foreach (var p in ChallengeSettings.All)
            {
                float v = Apply(p, p.FromConfig());
                if (!_locked.TryGetValue(Id(p), out var old) || old != v) { _locked[Id(p)] = v; changed = true; }
            }
            if (changed)
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FileFor(slot), JsonSerializer.Serialize(_locked, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception e) { Plugin.L.LogError($"Challenge lock failed: {e.Message}"); _locked = null; }
    }

    public static void Clear(int slot)
    {
        try
        {
            var f = FileFor(slot);
            if (File.Exists(f)) File.Move(f, f + ".old", true);
        }
        catch (Exception e) { Plugin.L.LogWarning($"Challenge lock clear failed: {e.Message}"); }
        if (slot == _slot) _locked = null;
    }
}

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.RestoreData))]
internal static class ChallengeLockLoadPatch { static void Postfix() => ChallengeLock.OnSaveTouched(); }

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.SaveData))]
internal static class ChallengeLockSavePatch { static void Postfix() => ChallengeLock.OnSaveTouched(); }

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.PrepareForNewGame))]
internal static class ChallengeLockNewGamePatch
{
    static void Postfix() { if (SaveManager.Instance != null) ChallengeLock.Clear(SaveManager.Instance.CurrentSlot); }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteSlotData), typeof(int))]
internal static class ChallengeLockDeletePatch { static void Postfix(int slot) => ChallengeLock.Clear(slot); }

// Values from 0.1 (sections Enemies / Loot) are carried into the Challenge keys once.
internal static class LegacyMigration
{
    public static void Run()
    {
        Move("Enemies", "HealthMultiplier", ChallengeSettings.EnemyHealth, v => v);
        Move("Enemies", "DamageMultiplier", ChallengeSettings.EnemyDamage, v => v);
        Move("Enemies", "SpeedMultiplier", ChallengeSettings.EnemySpeed, v => v);
        Move("Enemies", "AttackCooldownMultiplier", ChallengeSettings.EnemyAttackRate, v => v > 0 ? 1f / v : 1f);
        Move("Enemies", "DensityMultiplier", ChallengeSettings.EnemyCount, v => v);
        Move("Enemies", "PlayerDamageMultiplier", ChallengeSettings.PlayerDamage, v => v);
        Move("Loot", "EmptySlotWeight", ChallengeSettings.EmptyChest, v => v);
        Move("Loot", "UncommonWeight", ChallengeSettings.UncommonWeight, v => v);
        Move("Loot", "RareWeight", ChallengeSettings.RareWeight, v => v);
        Move("Loot", "EpicWeight", ChallengeSettings.EpicWeight, v => v);
        Move("Loot", "LegendaryWeight", ChallengeSettings.LegendaryWeight, v => v);
        Move("Loot", "ItemAmountMultiplier", ChallengeSettings.ItemAmount, v => v);
        Move("Loot", "RecipeChanceMultiplier", ChallengeSettings.RecipeChance, v => v);
        Move("Loot", "ExtraDropChance", ChallengeSettings.ExtraDropChance, v => v, vanilla: 0f);
    }

    static void Move(string section, string key, Param target, Func<float, float> convert, float vanilla = 1f)
    {
        try
        {
            var cat = MelonLoader.MelonPreferences.GetCategory("FellSellPlus_" + section)
                      ?? MelonLoader.MelonPreferences.CreateCategory("FellSellPlus_" + section);
            var old = cat.GetEntry<float>(key) ?? cat.CreateEntry(key, vanilla);
            if (Math.Abs(old.Value - vanilla) > 0.0001f && target.Entry.Value == -1f)
            {
                target.Entry.Value = convert(old.Value);
                Plugin.L.LogInfo($"Challenge: moved {section}.{key} = {old.Value} to {target.Section}.{target.Key}.");
            }
            cat.DeleteEntry(key);
        }
        catch (Exception e) { Plugin.L.LogWarning($"Challenge: legacy {section}.{key} not migrated: {e.Message}"); }
    }
}
