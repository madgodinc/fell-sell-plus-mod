using System;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Skills;

// Time Loop rewinds the floor instead of reviving in place. The first time the game saves on a new
// dungeon floor (its own autosave on arrival), a copy of that save is taken with the game's own
// SaveManager.CreateBackupSnapshot. On death the copy is restored with RestoreBackupFileToSlot and
// loaded through the game's normal Continue path, so inventory, gold, stats, prayers and the floor
// itself come back exactly as they were. The charge counter lives outside the save (otherwise the
// rewind would refund itself): one rewind per dungeon run, refilled in town.
internal static class TimeLoop
{
    const int ChargesPerRun = 1;
    static int _used;
    static int _snapshotFloor = -1;
    static string _snapshot;
    static float _rewindAt = -999f;
    // Devourer bonuses live in memory, not in the save: keep a copy with the snapshot so a rewind
    // cannot keep the bonus of items that come back.
    static System.Collections.Generic.Dictionary<Devourer.Stat, float> _devourAtSnapshot = new();

    // Called after every game save.
    public static void OnGameSaved()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene == "MAIN") { _used = 0; _snapshot = null; _snapshotFloor = -1; return; }
        if (scene != "DUNGEON") return;
        var dm = DungeonManager.Instance;
        var sm = SaveManager.Instance;
        if (dm == null || sm == null || dm.currentFloor == _snapshotFloor) return;
        // Snapshots only for players who have the skill (each is a save-file copy).
        if (PlayerSkillManager.Instance == null || PlayerSkillManager.Instance.GetSkillRank(SkillRegistry.MageTimeLoop) <= 0) return;
        try
        {
            _snapshot = sm.CreateBackupSnapshot(sm.CurrentSlot, "FSP_TimeLoop");
            _snapshotFloor = dm.currentFloor;
            _devourAtSnapshot = new System.Collections.Generic.Dictionary<Devourer.Stat, float>(Devourer.Total);
            Plugin.L.LogInfo($"Time Loop: floor {_snapshotFloor} start saved ({_snapshot}).");
        }
        catch (Exception e) { Plugin.L.LogWarning($"Time Loop snapshot failed: {e.Message}"); }
    }

    public static bool TryRewind()
    {
        // Death can fire more than once while the reload is starting: one rewind per 20 s at most.
        if (_used >= ChargesPerRun || string.IsNullOrEmpty(_snapshot) || UnityEngine.Time.unscaledTime - _rewindAt < 20f) return false;
        var dm = DungeonManager.Instance;
        var sm = SaveManager.Instance;
        if (dm == null || sm == null || dm.currentFloor != _snapshotFloor) return false;
        try
        {
            if (!sm.RestoreBackupFileToSlot(sm.CurrentSlot, _snapshot)) return false;
            var menu = UnityEngine.Object.FindObjectOfType<MainPauseMenu>();
            if (menu == null || !menu.CanContinue()) return false;
            _used++;
            _rewindAt = UnityEngine.Time.unscaledTime;
            Devourer.Total.Clear();
            foreach (var kv in _devourAtSnapshot) Devourer.Total[kv.Key] = kv.Value;
            Plugin.L.LogInfo("Time Loop: rewinding to the start of the floor.");
            menu.Continue();
            return true;
        }
        catch (Exception e) { Plugin.L.LogWarning($"Time Loop rewind failed: {e.Message}"); return false; }
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveGlobalPlayerState))]
internal static class TimeLoopSnapshotPatch
{
    static void Postfix() => TimeLoop.OnGameSaved();
}
