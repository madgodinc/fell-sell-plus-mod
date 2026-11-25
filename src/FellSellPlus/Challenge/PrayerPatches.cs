using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using C = FellSellPlus.Challenge.ChallengeSettings;

namespace FellSellPlus.Challenge;

// Prayers that never count toward PrayerLimit: ones granted without a statue (furniture bonus /
// initial prayer) and forced evil prayers.
internal static class PrayerBook
{
    public static readonly HashSet<IntPtr> Exempt = new();

    public static int TakenAtStatues()
    {
        var pm = PrayersManager.Instance;
        if (pm == null) return 0;
        var list = pm.GetActivePrayers();
        int n = 0;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && !Exempt.Contains(list[i].Pointer)) n++;
        return n;
    }

    public static bool IsGoodStatue(PrayersStatue s)
    {
        var pool = s.boonPool;
        if (pool == null || pool.Count == 0) return false;
        for (int i = 0; i < pool.Count; i++) if (pool[i] != null && pool[i].isEvil) return false;
        return true;
    }
}

[HarmonyPatch(typeof(PrayersManager), nameof(PrayersManager.ResetPrayers))]
internal static class PrayerResetPatch { static void Postfix() { PrayerBook.Exempt.Clear(); ForcedEvil.ResetRun(); } }

// Statue use: limit 0 disables statues; EvilPrayersOnly disables statues whose pool has no evil prayer.
[HarmonyPatch(typeof(PrayersStatue), nameof(PrayersStatue.Interact))]
internal static class StatueUsePatch
{
    static bool Prefix(PrayersStatue __instance)
    {
        if (C.PrayerLimit.Value < 0.5f) return false;
        if (C.EvilPrayersOnly.On && PrayerBook.IsGoodStatue(__instance)) return false;
        return true;
    }
}

// Limit for any run (the game itself caps only Dungeon+, at 5): at the limit the game's own
// replacement flow is started, exactly as it does for its Dungeon+ cap.
[HarmonyPatch(typeof(PrayersUI), nameof(PrayersUI.SelectBoon))]
internal static class PrayerLimitAnyRunPatch
{
    static bool Prefix(PrayersUI __instance, PrayerData chosenBoon)
    {
        try
        {
            if (chosenBoon == null || __instance._isReplacementMode) return true;
            if (ForcedEvil.Showing == chosenBoon.Pointer)
            {
                PrayerBook.Exempt.Add(chosenBoon.Pointer);
                ForcedEvil.Showing = IntPtr.Zero;
                return true;
            }
            if (__instance.currentStatue == null) { PrayerBook.Exempt.Add(chosenBoon.Pointer); return true; }

            int limit = (int)Math.Round(C.PrayerLimit.Value);
            if (limit >= 99 || PrayerBook.TakenAtStatues() < limit) return true;
            __instance.LockAllCards();
            __instance.StartCoroutine(__instance.EnterReplacementModeRoutine(chosenBoon));
            return false;
        }
        catch (Exception e) { Plugin.L.LogWarning($"Prayer limit: {e.Message}"); return true; }
    }
}

// Forced evil prayers: when a dungeon floor finishes spawning, show one random evil prayer that
// must be taken (skip and reroll are blocked while it is shown), up to N per run.
internal static class ForcedEvil
{
    public static IntPtr Showing;
    static int _lastFloor = -1, _countThisRun;

    public static void ResetRun() { _lastFloor = -1; _countThisRun = 0; Showing = IntPtr.Zero; }

    public static void OnFloorReady()
    {
        try
        {
            int want = (int)Math.Round(C.ForcedEvilPrayers.Value);
            var dm = DungeonManager.Instance;
            if (want <= 0 || dm == null) return;
            int floor = dm.currentFloor;
            if (floor == _lastFloor) return;           // restore / EnsureEnemiesExist re-entries
            if (floor < _lastFloor || floor <= 1) _countThisRun = 0;
            _lastFloor = floor;
            if (_countThisRun >= want) return;
            _countThisRun++;
            MelonCoroutines.Start(OpenLater());
        }
        catch (Exception e) { Plugin.L.LogWarning($"Forced evil prayer: {e.Message}"); }
    }

    static System.Collections.IEnumerator OpenLater()
    {
        float until = Time.unscaledTime + 2.5f;
        while (Time.unscaledTime < until) yield return null;
        var ui = PrayersUI.Instance;
        var pm = PrayersManager.Instance;
        if (ui == null || pm == null || ui.IsOpen) yield break;
        var roll = pm.RollRandomUnholyPrayers(1);
        if (roll == null || roll.Count == 0) yield break;
        Showing = roll[0].Pointer;
        string title = Progression.GameQuery.IsRussian() ? "Тьма требует жертвы" : "The dark demands a price";
        ui.OpenUI(null, roll, title);
    }
}

[HarmonyPatch(typeof(PrayersUI), nameof(PrayersUI.OnSkipClicked))]
internal static class ForcedSkipPatch { static bool Prefix() => ForcedEvil.Showing == IntPtr.Zero; }

[HarmonyPatch(typeof(PrayersUI), nameof(PrayersUI.OnRerollClicked))]
internal static class ForcedRerollPatch { static bool Prefix() => ForcedEvil.Showing == IntPtr.Zero; }
