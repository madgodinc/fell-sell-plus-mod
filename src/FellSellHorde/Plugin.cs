global using Il2Cpp;
using System;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(FellSellHorde.Plugin), "Fell & Sell Horde", "1.0.0", "MGI")]
[assembly: MelonGame("Art Games Studio SA", "Fell & Sell")]
[assembly: HarmonyDontPatchAll]

namespace FellSellHorde;

public class Plugin : MelonMod
{
    internal static MelonLogger.Instance L;
    static string _scene = "";
    static int _errors;

    public override void OnInitializeMelon()
    {
        FellSellShared.SaveBackup.Run(LoggerInstance); // before anything can touch a save
        L = LoggerInstance;
        HordeSettings.Bind();
        if (!HordeSettings.Enabled.Value) { L.Msg("Disabled in settings."); return; }
        HordeStatuePlacer.Register();
        var harmony = new HarmonyLib.Harmony("mgi.fellsellhorde");
        int ok = 0, failed = 0;
        foreach (var t in typeof(Plugin).Assembly.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); ok++; }
            catch (Exception e) { failed++; L.Error($"Patch {t.Name} failed, feature disabled: {e.Message}"); }
        }
        L.Msg($"Loaded. Patches: {ok} applied, {failed} failed. Horde runs start at the Reaper statue by the shop gate.");
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        _scene = sceneName ?? "";
        HordeStatuePlacer.OnSceneChanged();
        if (_scene != "DUNGEON")
        {
            if (HordeGen.Active) L.Msg("Horde run ended (left the dungeon).");
            HordeGen.Active = false;
            HordeDirector.Reset();
        }
    }

    public override void OnUpdate()
    {
        if (!HordeSettings.Enabled.Value || _errors >= 5) return;
        try
        {
            if (_scene == "MAIN") { ArmInput(); HordeStatuePlacer.Tick(); HordeWindow.Tick(); }
            if (_scene == "DUNGEON") HordeDirector.Tick();
        }
        catch (Exception e)
        {
            _errors++;
            L.Error($"Tick failed ({_errors}/5): {e}");
        }
    }

    public override void OnGUI()
    {
        if (_scene == "DUNGEON") HordeDirector.OnGUI();
        else if (_scene == "MAIN" && HordeGen.Armed) ArmBanner();
    }

    static void ArmInput()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (string.IsNullOrWhiteSpace(HordeSettings.ArmKey.Value) || !Enum.TryParse<UnityEngine.InputSystem.Key>(HordeSettings.ArmKey.Value, true, out var key)) return;
        if (!kb[key].wasPressedThisFrame) return;
        HordeGen.Armed = !HordeGen.Armed;
        L.Msg(HordeGen.Armed ? "Horde armed: the next dungeon entry is a horde run." : "Horde disarmed.");
    }

    static UnityEngine.GUIStyle _st;
    static void ArmBanner()
    {
        if (_st == null) { _st = new UnityEngine.GUIStyle { fontSize = 22, alignment = UnityEngine.TextAnchor.MiddleCenter }; _st.normal.textColor = new UnityEngine.Color(1f, 0.85f, 0.6f); }
        string t = HordeDirector.Ru ? "Орда ждёт: следующий вход в подземелье будет бесконечным режимом"
                                    : "The horde is waiting: the next dungeon entry is an endless run";
        UnityEngine.GUI.Label(new UnityEngine.Rect(0, 20, UnityEngine.Screen.width, 30), t, _st);
    }
}
