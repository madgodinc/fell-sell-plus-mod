using HarmonyLib;

namespace FellSellPlus.Coop;

// The game pauses itself when its window loses focus or is minimised: WindowsManager.OnApplicationFocus
// and OnApplicationPause both end in PauseForBackground. In co-op that would stop the host's world for
// everybody and leave a client standing still in front of live enemies: while co-op runs, that pause
// is skipped. The pause menu the player opens himself is not touched.
//
// Only PauseForBackground is hooked. Hooking the two Unity messages themselves crashed the game the
// moment the window got its focus back (breakpoint exception inside GameAssembly, both PCs): they are
// too short to take a detour.
internal static class CoopFocus
{
    [HarmonyPatch(typeof(WindowsManager), nameof(WindowsManager.PauseForBackground))]
    internal static class BackgroundPatch { static bool Prefix() => !(CoopSettings.On && CoopModule.Running); }
}
