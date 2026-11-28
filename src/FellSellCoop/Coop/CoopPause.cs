using UnityEngine;

namespace FellSellPlus.Coop;

// A player's own menu, inventory or map stops the game through WindowsManager. With a partner
// connected that would freeze the host's enemies for everyone, or leave a client standing still in
// front of live ones. The game has its own switch for "windows open, world running"
// (WindowsManager.SetForceUnpaused): it is held on while a partner is connected. The windows still open.
internal static class CoopPause
{
    static bool _held;
    static float _next;

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;
        var wm = WindowsManager.Instance;
        if (wm == null) { _held = false; return; }
        bool want = CoopSettings.NoPause.Value && CoopModule.HasPartner;
        if (want && !wm.forceUnpaused)
        {
            wm.SetForceUnpaused(true);
            if (!_held) Plugin.L.LogInfo("Co-op: a partner is connected, your menus no longer stop the game.");
            _held = true;
        }
        else if (!want && _held)
        {
            _held = false;
            wm.SetForceUnpaused(false);
            Plugin.L.LogInfo("Co-op: no partner connected, menus pause the game again.");
        }
    }
}
