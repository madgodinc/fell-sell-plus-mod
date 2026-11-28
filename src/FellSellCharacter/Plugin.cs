global using Il2Cpp;
using System;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(FellSellCharacter.Plugin), "Fell & Sell Character", "1.0.0", "MGI")]
[assembly: MelonGame("Art Games Studio SA", "Fell & Sell")]
[assembly: HarmonyDontPatchAll]

namespace FellSellCharacter;

public class Plugin : MelonMod
{
    internal static MelonLogger.Instance L;
    internal static string Scene = "";
    internal static bool InGame => Scene == "MAIN" || Scene == "DUNGEON" || Scene == "FOREST";

    public override void OnInitializeMelon()
    {
        FellSellShared.SaveBackup.Run(LoggerInstance); // before anything can touch a save
        L = LoggerInstance;
        Settings.Bind();
        if (!Settings.Enabled.Value) { L.Msg("Disabled in settings."); return; }
        WardrobePlacer.Register();
        // Patch class by class so one broken hook (after a game update) disables only its feature.
        var harmony = new HarmonyLib.Harmony("mgi.fellsellcharacter");
        int ok = 0, failed = 0;
        foreach (var t in typeof(Plugin).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); ok++; }
            catch (Exception e) { failed++; L.Error($"Patch {t.Name} failed, feature disabled: {e.Message}"); }
        }
        L.Msg($"Loaded. Patches: {ok} applied, {failed} failed.");
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        Scene = sceneName ?? "";
        if (!Settings.Enabled.Value) return;
        try { ThirdPerson.OnSceneChanged(); WardrobePlacer.OnSceneChanged(); } catch (Exception e) { Fail("Scene change", e); }
    }

    public override void OnUpdate()
    {
        if (!Settings.Enabled.Value) return;
        try { Dev.Tick(); } catch (Exception e) { Fail("Dev", e); }
        if (!InGame) return; // game singletons only in gameplay scenes
        try { ThirdPerson.Tick(); } catch (Exception e) { Fail("View key", e); }
        try { Editor.Tick(); } catch (Exception e) { Fail("Editor", e); }
        if (Scene == "MAIN") try { WardrobePlacer.Tick(); } catch (Exception e) { Fail("Wardrobe", e); }
    }

    public override void OnLateUpdate()
    {
        if (!Settings.Enabled.Value || !InGame || ThirdPerson.LateHookAlive) return;
        try { ThirdPerson.LateTick(); } catch (Exception e) { Fail("Body update", e); }
    }

    // A broken feature must not flood the log every frame: each kind of failure is reported a few times.
    static readonly System.Collections.Generic.Dictionary<string, int> Fails = new();
    internal static void Fail(string what, Exception e)
    {
        Fails.TryGetValue(what, out int n);
        Fails[what] = n + 1;
        if (n < 3) L.Error($"{what}: {e}");
    }
}
