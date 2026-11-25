using System;
using System.IO;
using MelonLoader.Utils;

namespace FellSellPlus;

// On this game (Unity 6000.3, IL2CPP) the .NET tiered JIT re-compiles hot mod methods on a
// background thread; when such a method touches IL2CPP interop types the runtime crashes
// natively (AV in coreclr, reproduced repeatedly; gone with tiering off). Tiering is a
// startup-only setting, so the mod adds it to MelonLoader's runtimeconfig and it applies from the
// next launch.
internal static class RuntimeGuard
{
    const string Key = "\"System.Runtime.TieredCompilation\"";

    public static void Ensure()
    {
        try
        {
            var path = Path.Combine(MelonEnvironment.MelonLoaderDirectory, "net6", "MelonLoader.runtimeconfig.json");
            if (!File.Exists(path)) return;
            var text = File.ReadAllText(path);
            if (text.Contains(Key)) return;
            const string anchor = "\"configProperties\": {";
            int i = text.IndexOf(anchor, StringComparison.Ordinal);
            if (i < 0) { Plugin.L.LogWarning("RuntimeGuard: runtimeconfig layout not recognised; tiered JIT left on."); return; }
            File.Copy(path, path + ".fsp-backup", true);
            text = text.Insert(i + anchor.Length, "\n      " + Key + ": false,");
            File.WriteAllText(path, text);
            Plugin.L.LogWarning("Fell & Sell Plus turned off .NET tiered compilation (prevents random crashes). Restart the game once.");
        }
        catch (Exception e) { Plugin.L.LogWarning($"RuntimeGuard failed: {e.Message}"); }
    }
}
