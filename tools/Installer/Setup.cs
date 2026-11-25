using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FellSellPlus.Setup;

// Everything the setup does, without UI: find the game, check .NET, install, uninstall.
internal static class Setup
{
    public const string AppId = "4627110";
    public const string GameExe = "Fell & Sell.exe";
    const string ShortcutName = "Fell & Sell Plus Settings.lnk";

    // ---- finding the game ----

    public static string FindGame()
    {
        foreach (var lib in SteamLibraries())
        {
            var dir = Path.Combine(lib, "steamapps", "common", "Fell & Sell");
            if (IsGame(dir)) return dir;
        }
        return null;
    }

    public static bool IsGame(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, GameExe));

    static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        void Add(string p) { if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && !roots.Contains(p, StringComparer.OrdinalIgnoreCase)) roots.Add(p.Replace('/', '\\')); }
        try { Add(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string); } catch { }
        try { Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string); } catch { }
        Add(@"C:\Program Files (x86)\Steam");
        var libs = new List<string>(roots);
        foreach (var r in roots)
        {
            var vdf = Path.Combine(r, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(p) && !libs.Contains(p, StringComparer.OrdinalIgnoreCase)) libs.Add(p);
            }
        }
        foreach (var d in "DEFGH") Add(d + @":\SteamLibrary");
        return libs.Concat(roots).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static bool GameRunning() => Process.GetProcessesByName("Fell & Sell").Length > 0;

    public static bool Installed(string game) => IsGame(game) && File.Exists(Path.Combine(game, "Mods", "FellSellPlus.dll"));

    // ---- .NET ----

    // MelonLoader needs the .NET runtime (6 or newer); the configurator needs the Windows Desktop
    // runtime 8 or newer. One Windows Desktop 8+ install covers both.
    public static bool HasDesktopRuntime()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
            if (k != null && k.GetValueNames().Any(v => Major(v) >= 8)) return true;
        }
        catch { }
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        return Directory.Exists(dir) && Directory.GetDirectories(dir).Any(d => Major(Path.GetFileName(d)) >= 8);
    }

    static int Major(string v) => int.TryParse(v.Split('.')[0], out var m) ? m : 0;

    public const string RuntimeUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";

    // Downloads Microsoft's installer and runs it quietly (Windows asks for admin once).
    public static void InstallRuntime(Action<string> say)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var file = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-8-x64.exe");
        say("download");
        using (var wc = new WebClient()) wc.DownloadFile(RuntimeUrl, file);
        say("install");
        var p = Process.Start(new ProcessStartInfo(file, "/install /quiet /norestart") { UseShellExecute = true, Verb = "runas" });
        p.WaitForExit();
        if (p.ExitCode != 0 && p.ExitCode != 3010) throw new Exception(".NET installer exit code " + p.ExitCode);
    }

    // ---- install ----

    public static string SavesDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Art Games Studio SA", "Fell _ Sell");
    public static string BackupRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Fell & Sell Plus", "Save backups");

    public static string BackupSaves()
    {
        if (!Directory.Exists(SavesDir)) return null;
        var dest = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        CopyDir(SavesDir, dest);
        return dest;
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    // Unpacks the payload (laid out as the game folder) over the game folder.
    public static void Unpack(string game)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                      ?? throw new Exception("payload.zip is missing from the setup");
        using var zip = new ZipArchive(s, ZipArchiveMode.Read);
        foreach (var e in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(game, e.FullName));
            if (!target.StartsWith(Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase)) continue;
            if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            e.ExtractToFile(target, true);
        }
    }

    // .NET tiered compilation crashes this Unity build under MelonLoader (see the mod's RuntimeGuard).
    public static void TieredCompilationOff(string game)
    {
        var rc = Path.Combine(game, "MelonLoader", "net6", "MelonLoader.runtimeconfig.json");
        if (!File.Exists(rc)) return;
        var text = File.ReadAllText(rc);
        if (text.Contains("TieredCompilation")) return;
        text = Regex.Replace(text, "\"configProperties\"\\s*:\\s*\\{", "\"configProperties\": {\n      \"System.Runtime.TieredCompilation\": false,");
        File.WriteAllText(rc, text);
    }

    public static string ConfiguratorExe(string game) => Path.Combine(game, "UserData", "FellSellPlus", "Configurator", "FellSellPlusConfigurator.exe");

    static IEnumerable<string> ShortcutPaths()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
    }

    public static void Shortcuts(string game)
    {
        var exe = ConfiguratorExe(game);
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        foreach (var lnk in ShortcutPaths())
        {
            dynamic sc = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            sc.TargetPath = exe;
            sc.WorkingDirectory = Path.GetDirectoryName(exe);
            sc.Description = "Fell & Sell Plus settings";
            sc.Save();
        }
    }

    // ---- uninstall ----

    public static void Uninstall(string game, bool melonLoaderToo)
    {
        Delete(Path.Combine(game, "Mods", "FellSellPlus.dll"));
        DeleteDir(Path.Combine(game, "UserData", "FellSellPlus"));
        foreach (var lnk in ShortcutPaths()) Delete(lnk);
        if (!melonLoaderToo) return;
        // No other mods: everything MelonLoader added goes, so the game starts as it shipped.
        foreach (var d in new[] { "MelonLoader", "Mods", "Plugins", "UserLibs", "UserData" }) DeleteDir(Path.Combine(game, d));
        foreach (var f in new[] { "version.dll", "dobby.dll", "NOTICE.txt" }) Delete(Path.Combine(game, f));
    }

    public static bool OtherMods(string game)
    {
        var mods = Path.Combine(game, "Mods");
        return Directory.Exists(mods) && Directory.GetFiles(mods, "*.dll").Any(f => !Path.GetFileName(f).Equals("FellSellPlus.dll", StringComparison.OrdinalIgnoreCase));
    }

    static void Delete(string f) { if (File.Exists(f)) File.Delete(f); }
    static void DeleteDir(string d) { if (Directory.Exists(d)) Directory.Delete(d, true); }

    public static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".fsp-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}
