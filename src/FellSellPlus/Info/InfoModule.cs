using System;

namespace FellSellPlus.Info;

// Entry points for Plugin: Bind() in OnInitializeMelon, Tick() in OnUpdate.
// Hooks (PrayerHints, RunRecorder.*Hook) are [HarmonyPatch] classes picked up by Plugin's patch loop.
internal static class InfoModule
{
    public static void Bind(ConfigFile cfg) => InfoSettings.Bind(cfg);

    public static void Tick()
    {
        Safe(RunRecorder.Tick); // reads only the scene name outside dungeon/forest, so it also sees quits to the menu
        // Game singletons are only safe in gameplay scenes (touching them on the splash screen crashed the game).
        var scene = Inventory.ItemCounts.Scene();
        if (scene != "MAIN" && scene != "DUNGEON" && scene != "FOREST") return;
        Safe(RunsWindow.Tick);
        Safe(StatsPanel.Tick);
        Safe(SkillBookOdds.Tick);
        Safe(LootOdds.Tick);
    }

    static void Safe(Action a)
    {
        try { a(); } catch (Exception e) { Plugin.L.LogWarning($"Info: {e.Message}"); }
    }
}
