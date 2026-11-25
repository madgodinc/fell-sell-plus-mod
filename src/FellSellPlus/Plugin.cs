using System;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(FellSellPlus.Plugin), "Fell & Sell Plus", "1.0.1", "MGI")]
[assembly: MelonGame("Art Games Studio SA", "Fell & Sell")]
// MelonLoader would otherwise PatchAll this assembly on top of our own per-class loop, running
// every hook twice (doubled HP gains, squared multipliers, duplicated tooltip lines).
[assembly: HarmonyDontPatchAll]
// The co-op mod (a separate assembly) uses the config layer, log and player data of this mod.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FellSellCoop")]

namespace FellSellPlus;

public class Plugin : MelonMod
{
    public const string Guid = "mgi.fellsellplus";
    internal static readonly Log L = new();
    internal static readonly ConfigFile Config = new();

    public override void OnInitializeMelon()
    {
        RuntimeGuard.Ensure();
        Settings.Bind(Config);
        Prayers.NewPrayers.Bind(Config);
        Progression.ProgressionSettings.Bind(Config);
        Challenge.ChallengeModule.Init(Config);
        Inventory.InventoryModule.Bind(Config);
        FellSellPlus.Info.InfoModule.Bind(Config);
        Shop.ShopSettings.Bind(Config);
        Crafting.CraftSettings.Bind(Config);
        Skills.LevelCap.Bind(Config);
        Dev.DevCommands.Bind(Config);
        ConsoleHide.Bind(Config);
        var skip = Config.Bind("Dev", "SkipModules", "", "Developer bisect: comma-separated namespaces (e.g. Inventory,Shop) whose patches and ticks are disabled. Leave empty.").Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Skipped = new System.Collections.Generic.HashSet<string>(skip);
        MelonPreferences.Save();
        Config.WriteSchema();

        // Patch class by class so one broken hook (after a game update) disables only its feature.
        var harmony = new HarmonyLib.Harmony(Guid);
        int ok = 0, failed = 0;
        foreach (var t in typeof(Plugin).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            if (IsSkipped(t)) continue;
            try { harmony.CreateClassProcessor(t).Patch(); ok++; }
            catch (Exception e) { failed++; L.LogError($"Patch {t.Name} failed, feature disabled: {e.Message}"); }
        }
        PrayerLimit.Apply();
        Progression.ProgressionModule.Init();
        L.LogInfo($"Loaded. Patches: {ok} applied, {failed} failed.");
        ConsoleHide.Apply();
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        // From the main menu on (not the splash): whatever art is loaded is exported, the rest on later scenes.
        if (buildIndex > 0) ArtExport.OnScene();
    }

    public override void OnUpdate()
    {
        Tick(Progression.ProgressionDriver.Tick);
        Tick(Inventory.InventoryModule.Tick);
        Tick(FellSellPlus.Info.InfoModule.Tick);
        Tick(Shop.ShopModule.Tick);
        Tick(Crafting.CraftModule.Tick);
        Tick(Skills.Devourer.Tick);
        Tick(FellSellPlus.Info.EnemyBars.Tick);
        Tick(Dev.DevCommands.Tick);
    }

    // One module throwing every frame must not take the others down or flood the log.
    static readonly System.Collections.Generic.Dictionary<Action, int> Errors = new();
    static System.Collections.Generic.HashSet<string> Skipped = new();
    internal static bool Skip(string name) => Skipped.Contains(name);
    static bool IsSkipped(Type t) => t != null && (Skipped.Contains(t.Name) || (t.Namespace != null && Skipped.Contains(t.Namespace.Replace("FellSellPlus.", ""))));

    static void Tick(Action a)
    {
        if (IsSkipped(a.Method.DeclaringType)) return;
        if (Errors.TryGetValue(a, out var n) && n >= 5) return;
        try { a(); }
        catch (Exception e)
        {
            Errors[a] = n + 1;
            L.LogError($"{a.Method.DeclaringType?.Name}.Tick failed ({n + 1}/5): {e}");
        }
    }
}
