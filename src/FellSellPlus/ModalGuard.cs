using HarmonyLib;
using UnityEngine;

namespace FellSellPlus;

// Mod windows sit in the game's own window stack (GameWindows), so Esc is left to the game: it closes
// the top window and opens the pause menu only when no window is open. Windows still report
// themselves open every frame so the game's inventory key is held back while one is up.
internal static class ModalGuard
{
    static int _lastOpenFrame = -100;
    public static void Touch() => _lastOpenFrame = Time.frameCount;
    public static bool Active => Time.frameCount - _lastOpenFrame <= 2;
}



[HarmonyPatch(typeof(WindowsManager), "HandleInventoryInput")]
internal static class InventoryInputGuardPatch
{
    static bool Prefix() => !ModalGuard.Active;
}

// Mod windows go through the game's own WindowsManager (the same OpenWindow/CloseWindow every game
// window uses): it owns the cursor, input blocking and pause, so mouse clicks work and the game does
// not grab the cursor back. Setting Cursor.lockState ourselves was overridden by the game each frame.
internal static class GameWindows
{
    public static void Open(GameObject root)
    {
        var wm = WindowsManager.Instance;
        if (wm == null || root == null) return;
        try
        {
            if (!wm.IsWindowInStack(root)) { wm.RegisterWindow(root); wm.OpenWindow(root); }
        }
        catch (System.Exception e) { Plugin.L.LogWarning($"Window open via WindowsManager failed: {e.Message}"); }
    }

    public static void Close(GameObject root)
    {
        var wm = WindowsManager.Instance;
        if (wm == null || root == null) return;
        try { wm.CloseWindow(root); wm.UnregisterWindow(root); }
        catch (System.Exception e) { Plugin.L.LogWarning($"Window close via WindowsManager failed: {e.Message}"); }
    }
}
