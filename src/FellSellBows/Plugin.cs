global using Il2Cpp;
using System;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(FellSellBows.Plugin), "Fell & Sell Bows", "1.0.0", "MGI")]
[assembly: MelonGame("Art Games Studio SA", "Fell & Sell")]
[assembly: HarmonyDontPatchAll]

namespace FellSellBows;

public class Plugin : MelonMod
{
    internal static MelonLogger.Instance L;

    public override void OnInitializeMelon()
    {
        FellSellShared.SaveBackup.Run(LoggerInstance); // before anything can touch a save
        L = LoggerInstance;
        // Patch class by class so one broken hook (after a game update) disables only its feature.
        var harmony = new HarmonyLib.Harmony("mgi.fellsellbows");
        int ok = 0, failed = 0;
        foreach (var t in typeof(Plugin).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); ok++; }
            catch (Exception e) { failed++; L.Error($"Patch {t.Name} failed, feature disabled: {e.Message}"); }
        }
        L.Msg($"Loaded. Patches: {ok} applied, {failed} failed.");
    }

    static string _scene = "";
    public override void OnUpdate()
    {
        if (_scene != "MAIN" && _scene != "DUNGEON" && _scene != "FOREST") return; // game singletons only in gameplay scenes
        try { BowHands.Tick(); } catch (Exception e) { L.Error("Bow tick: " + e.Message); }
        try { BowDraw.InputTick(); } catch (Exception e) { L.Error("Bow input: " + e.Message); }
    }

    public override void OnLateUpdate()
    {
        if (_scene != "MAIN" && _scene != "DUNGEON" && _scene != "FOREST") return;
        try { if (!BowDraw.HookAlive) BowDraw.LateTick(); } catch (Exception e) { L.Error("Bow draw: " + e.Message); }
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        // Enemy archers (and their bow and arrow meshes) load only with some scenes:
        // each scene gets another chance to swap the placeholder staff visuals for the real ones.
        _scene = sceneName ?? "";
        if (buildIndex <= 0) return;
        try { Bows.Ensure(); Bows.UpgradeVisuals(sceneName); }
        catch (Exception e) { L.Error("Scene setup failed: " + e); }
    }
}
