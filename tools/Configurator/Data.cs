using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FellSellPlus.Configurator;

// One setting of the mod: its schema plus the current value as text (cfg format, unquoted).
public sealed class Setting
{
    public string Section, Key, Type, Description;
    public double? Min, Max, Vanilla;
    public double[] Presets;           // Challenge only: Veteran, Nightmare, Blasphemous, Insane
    public string Default, Value;

    public string Id => Section.Replace("FellSellPlus_", "") + "." + Key;
    public bool IsChallengeParam => Section.StartsWith("FellSellPlus_Challenge_");
    public bool Changed => !Same(Value, Default);

    public static bool Same(string a, string b)
    {
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return Math.Abs(x - y) < 1e-6;
        return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

public static class Data
{
    public static readonly string[] PresetNames = { "Off", "Veteran", "Nightmare", "Blasphemous", "Insane" };

    // Schema: the mod's own export when the game has run with the mod, else the copy built from sources.
    public static List<Setting> LoadSchema(string game)
    {

        var exported = game == null ? null : Path.Combine(game, "UserData", "FellSellPlus", "config_schema.json");
        var embedded = ReadResource("schema.json");
        var fromMod = exported != null && File.Exists(exported) ? File.ReadAllText(exported) : null;
        var list = Parse(embedded);
        if (fromMod != null)
        {
            // The mod's export knows the real ranges; keep the embedded extras (Challenge presets).
            var byId = list.ToDictionary(s => s.Section + "|" + s.Key);
            foreach (var m in Parse(fromMod))
            {
                if (byId.TryGetValue(m.Section + "|" + m.Key, out var e))
                { e.Min = m.Min ?? e.Min; e.Max = m.Max ?? e.Max; e.Default = m.Default ?? e.Default; e.Type = m.Type ?? e.Type; }
                else { list.Add(m); byId[m.Section + "|" + m.Key] = m; }
            }
        }
        return list;
    }

    static List<Setting> Parse(string json)
    {
        var res = new List<Setting>();
        if (string.IsNullOrEmpty(json)) return res;
        var root = JsonNode.Parse(json);
        foreach (var e in root["entries"].AsArray())
        {
            var s = new Setting
            {
                Section = (string)e["section"], Key = (string)e["key"], Type = (string)e["type"],
                Description = (string)e["description"] ?? "",
                Min = Num(e["min"]), Max = Num(e["max"]), Vanilla = Num(e["vanilla"]),
                Default = Text(e["default"]),
            };
            if (e["presets"] is JsonArray pa) s.Presets = pa.Select(x => (double)x).ToArray();
            s.Value = s.Default;
            res.Add(s);
        }
        return res;
    }

    static double? Num(JsonNode n) => n == null ? null : n.GetValueKind() == JsonValueKind.Number ? (double)n : null;
    static string Text(JsonNode n)
    {
        if (n == null) return "";
        return n.GetValueKind() switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => ((double)n).ToString("0.######", CultureInfo.InvariantCulture),
            _ => (string)n,
        };
    }

    public static string ReadResource(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (s == null) return null;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    // ---- MelonPreferences.cfg: [Section], "# comment" lines, "Key = value" ----

    public static string CfgPath(string game) => Path.Combine(game, "UserData", "MelonPreferences.cfg");

    public static void ReadValues(string game, List<Setting> settings)
    {
        var path = CfgPath(game);
        if (!File.Exists(path)) return;
        var by = settings.ToDictionary(s => s.Section + "|" + s.Key);
        string section = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line[1..^1]; continue; }
            if (section == null || line.StartsWith("#") || !line.Contains('=')) continue;
            int eq = line.IndexOf('=');
            var key = line[..eq].Trim();
            if (by.TryGetValue(section + "|" + key, out var s)) s.Value = Unquote(line[(eq + 1)..].Trim());
        }
    }

    // Values are rewritten in place; other mods' sections, comments and order are untouched.
    // Settings missing from the file are appended to their section (or a new section at the end).
    public static void WriteValues(string game, List<Setting> settings)
    {
        var path = CfgPath(game);
        var original = File.Exists(path) ? File.ReadAllText(path) : "";
        string nl = original.Contains("\r\n") ? "\r\n" : "\n";      // keep the file's own line endings
        bool trailing = original.EndsWith("\n");
        var lines = original.Length == 0 ? new List<string>() : original.Replace("\r\n", "\n").Split('\n').ToList();
        if (trailing && lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var pending = settings.ToDictionary(s => s.Section + "|" + s.Key);
        string section = null;
        int sectionEnd = -1;
        var sectionEnds = new Dictionary<string, int>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line[1..^1]; sectionEnds[section] = i + 1; continue; }
            if (section == null) continue;
            if (line.Length > 0) sectionEnds[section] = i + 1;
            if (line.StartsWith("#") || !line.Contains('=')) continue;
            int eq = line.IndexOf('=');
            var key = line[..eq].Trim();
            if (pending.TryGetValue(section + "|" + key, out var s))
            {
                var indent = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
                lines[i] = $"{indent}{key} = {Format(s)}";
                pending.Remove(section + "|" + key);
            }
        }
        _ = sectionEnd;
        // Append the rest, section by section, inserting from the bottom so indices stay valid.
        foreach (var grp in pending.Values.GroupBy(s => s.Section).OrderByDescending(g => sectionEnds.GetValueOrDefault(g.Key, int.MaxValue)))
        {
            var add = new List<string>();
            foreach (var s in grp)
            {
                if (!string.IsNullOrWhiteSpace(s.Description)) add.Add("# " + s.Description);
                add.Add($"{s.Key} = {Format(s)}");
            }
            if (sectionEnds.TryGetValue(grp.Key, out var at)) lines.InsertRange(at, add);
            else { lines.Add(""); lines.Add($"[{grp.Key}]"); lines.AddRange(add); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.WriteAllText(path, string.Join(nl, lines) + (trailing || original.Length == 0 ? nl : ""), new UTF8Encoding(false));
    }

    static string Unquote(string v) => v.Length >= 2 && v.StartsWith('"') && v.EndsWith('"') ? v[1..^1].Replace("\\\"", "\"") : v;

    static string Format(Setting s) => s.Type switch
    {
        "Boolean" => s.Value?.Trim().ToLowerInvariant() == "true" ? "true" : "false",
        "Int32" => long.TryParse(s.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i.ToString(CultureInfo.InvariantCulture) : s.Default,
        "Single" => double.TryParse(s.Value?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? (d == Math.Floor(d) ? d.ToString("0.0", CultureInfo.InvariantCulture) : d.ToString("0.######", CultureInfo.InvariantCulture)) : s.Default,
        _ => "\"" + (s.Value ?? "").Replace("\"", "\\\"") + "\"",
    };

    // ---- game folder ----

    static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FellSellPlus", "configurator.json");

    public static string FindGame()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var saved = (string)JsonNode.Parse(File.ReadAllText(SettingsFile))?["gamePath"];
                if (IsGame(saved)) return saved;
            }
        }
        catch { }
        foreach (var c in new[]
                 {
                     @"C:\Program Files (x86)\Steam\steamapps\common\Fell & Sell",
                     @"C:\Program Files\Steam\steamapps\common\Fell & Sell",
                     @"D:\SteamLibrary\steamapps\common\Fell & Sell",
                     @"E:\SteamLibrary\steamapps\common\Fell & Sell",
                 })
            if (IsGame(c)) return c;
        return null;
    }

    public static bool IsGame(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "Fell & Sell.exe"));

    public static void Remember(string game, string lang)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
        File.WriteAllText(SettingsFile, new JsonObject { ["gamePath"] = game, ["lang"] = lang }.ToJsonString());
    }

    public static string RememberedLang()
    {
        // null = never chosen: the window then follows the PC's language.
        try { return File.Exists(SettingsFile) ? (string)JsonNode.Parse(File.ReadAllText(SettingsFile))?["lang"] : null; }
        catch { return null; }
    }

    public static bool GameRunning() => Process.GetProcessesByName("Fell & Sell").Length > 0;
}

// Russian / English names and one-line explanations.
public static class Texts
{
    static JsonNode _t;
    static JsonNode T => _t ??= JsonNode.Parse(Data.ReadResource("texts.json"));

    public static (string name, string hint) Key(string id, string lang, Setting s)
    {
        var n = T["keys"]?[id]?[lang];
        if (n is JsonArray a && a.Count >= 2) return ((string)a[0], (string)a[1]);
        return (s.Key, s.Description);
    }

    public static (string title, string sub) Section(string sec, string lang)
    {
        var n = T["sections"]?[sec]?[lang] as JsonArray;
        return n != null ? ((string)n[0], (string)n[1]) : (sec, "");
    }

    public static string Group(string g, string lang) => (string)T["groups"]?[g]?[lang] ?? g;
}

// "Restore the game": removes everything the setup added so the game starts as it shipped. If other
// MelonLoader mods are present, the loader stays so they keep working. Saves are never touched.
public static class Restore
{
    public static bool OtherMods(string game)
    {
        var mods = Path.Combine(game, "Mods");
        return Directory.Exists(mods) && Directory.GetFiles(mods, "*.dll").Any(f => !Path.GetFileName(f).Equals("FellSellPlus.dll", StringComparison.OrdinalIgnoreCase));
    }

    public static void Run(string game)
    {
        bool keepLoader = OtherMods(game);
        File.Delete(Path.Combine(game, "Mods", "FellSellPlus.dll"));
        foreach (var lnk in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Fell & Sell Plus Settings.lnk"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Fell & Sell Plus Settings.lnk"),
                 })
            if (File.Exists(lnk)) File.Delete(lnk);

        string later; // folders that hold this running app: removed by a hidden helper after exit
        if (keepLoader)
            later = Path.Combine(game, "UserData", "FellSellPlus");
        else
        {
            foreach (var d in new[] { "MelonLoader", "Mods", "Plugins", "UserLibs" })
                if (Directory.Exists(Path.Combine(game, d))) Directory.Delete(Path.Combine(game, d), true);
            foreach (var f in new[] { "version.dll", "dobby.dll", "NOTICE.txt" })
                if (File.Exists(Path.Combine(game, f))) File.Delete(Path.Combine(game, f));
            later = Path.Combine(game, "UserData");
        }
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{later}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
