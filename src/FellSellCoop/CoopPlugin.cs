using System;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(FellSellCoop.CoopPlugin), "Fell & Sell Coop", "1.0.0", "MGI")]
[assembly: MelonGame("Art Games Studio SA", "Fell & Sell")]
[assembly: MelonAdditionalDependencies("FellSellPlus")]
[assembly: MelonOptionalDependencies("FellSellCharacter", "FellSellHorde")]
[assembly: HarmonyDontPatchAll]

namespace FellSellCoop;

// Entry point of the co-op mod. It needs Fell & Sell Plus (its config layer, log and player data),
// so MelonLoader loads it after that mod. Patches are applied class by class like in the main mod.
public class CoopPlugin : MelonMod
{
    internal static readonly FellSellPlus.ConfigFile Config = new();

    public override void OnInitializeMelon()
    {
        FellSellPlus.Coop.CoopSettings.Bind(Config);
        MelonPreferences.Save();

        var harmony = new HarmonyLib.Harmony("mgi.fellsellcoop");
        int ok = 0, failed = 0;
        // Types that name an optional mod (Character, Horde) cannot be loaded without it: skip those.
        Type[] types;
        try { types = typeof(CoopPlugin).Assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
        foreach (var t in types)
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); ok++; }
            catch (Exception e) { failed++; FellSellPlus.Plugin.L.LogError($"Co-op patch {t.Name} failed: {e.Message}"); }
        }
        FellSellPlus.Plugin.L.LogInfo($"Co-op loaded. Patches: {ok} applied, {failed} failed.");
    }

    public override void OnUpdate()
    {
        try
        {
            FellSellPlus.Coop.HordeBridge.Hook();
            FellSellPlus.Coop.CoopMenu.Tick();
            FellSellPlus.Coop.CoopDev.Tick();
            FellSellPlus.Coop.CoopModule.Tick();
        }
        catch (Exception e) { FellSellPlus.Plugin.L.LogError($"Co-op tick: {e.Message}"); }
    }

    public override void OnLateUpdate() => FellSellPlus.Coop.CoopModule.LateTick();

    public override void OnApplicationQuit() => FellSellPlus.Coop.SteamLobby.Stop();
}
