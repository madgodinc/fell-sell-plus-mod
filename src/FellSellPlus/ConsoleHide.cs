using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MelonLoader.Utils;

namespace FellSellPlus;

// MelonLoader's console window is hidden for players. It stays visible during the very first
// launch, while MelonLoader prepares the game code for minutes, so the player can see the game is
// not frozen; once the mod is loaded the window is hidden right away and Loader.cfg is set to never
// show it again. The log keeps going to MelonLoader/Latest.log. [Console] ShowLoaderConsole = true
// brings it back.
internal static class ConsoleHide
{
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    public static ConfigEntry<bool> Show;

    public static void Bind(ConfigFile cfg)
    {
        Show = cfg.Bind("Console", "ShowLoaderConsole", false,
            "Show MelonLoader's black console window with the log. Off for players; the log is always in MelonLoader/Latest.log.");
    }

    public static void Apply()
    {
        try
        {
            bool show = Show?.Value ?? false;
            SetLoaderCfg(!show);
            if (!show)
            {
                var h = GetConsoleWindow();
                if (h != IntPtr.Zero) ShowWindow(h, 0); // SW_HIDE
            }
        }
        catch (Exception e) { Plugin.L.LogWarning($"Console hide: {e.Message}"); }
    }

    // UserData/Loader.cfg: [console] hide_console = true/false.
    static void SetLoaderCfg(bool hide)
    {
        var path = Path.Combine(MelonEnvironment.UserDataDirectory, "Loader.cfg");
        if (!File.Exists(path)) return;
        var text = File.ReadAllText(path);
        var want = "hide_console = " + (hide ? "true" : "false");
        var re = new Regex(@"hide_console\s*=\s*(true|false)");
        if (!re.IsMatch(text) || re.Match(text).Value == want) return;
        File.WriteAllText(path, re.Replace(text, want, 1));
    }
}
