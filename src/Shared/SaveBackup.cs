using System;
using System.IO;
using System.Linq;

namespace FellSellShared;

// Every Fell & Sell mod of ours calls this once when it loads: the save slots are copied into
// UserData/SaveBackups before the game touches them. The first backup ever ("first-install") is
// kept for good; later ones rotate (the newest 10). Several mods loading in the same launch make
// one backup, not one each.
internal static class SaveBackup
{
    const int Keep = 10;

    internal static void Run(MelonLoader.MelonLogger.Instance log)
    {
        try
        {
            string saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Art Games Studio SA", "Fell _ Sell");
            if (!Directory.Exists(saves)) return;
            var files = Directory.GetFiles(saves, "SaveSlot_*.es3");
            if (files.Length == 0) return;

            string root = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "SaveBackups");
            Directory.CreateDirectory(root);
            var dirs = Directory.GetDirectories(root).Select(d => new DirectoryInfo(d)).ToList();
            var rotating = dirs.Where(d => !d.Name.StartsWith("first-install")).OrderByDescending(d => d.CreationTimeUtc).ToList();
            if (dirs.Any(d => DateTime.UtcNow - d.CreationTimeUtc < TimeSpan.FromMinutes(2))) return; // another mod just did it

            string name = dirs.Any(d => d.Name.StartsWith("first-install"))
                ? DateTime.Now.ToString("yyyyMMdd-HHmmss")
                : "first-install-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dest = Path.Combine(root, name);
            Directory.CreateDirectory(dest);
            foreach (var f in files) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            log.Msg($"Saves backed up to UserData/SaveBackups/{name} ({files.Length} file(s)).");

            foreach (var old in rotating.Skip(Keep - 1))
                try { old.Delete(true); } catch { }
        }
        catch (Exception e) { log.Warning("Save backup failed: " + e.Message); }
    }
}
