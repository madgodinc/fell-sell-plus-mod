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
        list.AddRange(HordeSettings());
        list.AddRange(CharacterSettings());
        // Co-op binds through the main mod's config layer, so the mod's own export may already list it.
        foreach (var c in CoopSettings())
            if (!list.Any(s => s.Section == c.Section && s.Key == c.Key)) list.Add(c);
        return list;
    }

    // The horde add-on's player-facing settings (its MelonPreferences category is "FellSellHorde").
    static IEnumerable<Setting> HordeSettings()
    {
        Setting S(string key, string type, string def, double? min = null, double? max = null) =>
            new() { Section = "FellSellHorde", Key = key, Type = type, Default = def, Value = def, Min = min, Max = max, Description = "" };
        yield return S("MaxAlive", "Int32", "50", 10, 50);
        yield return S("IntermissionSeconds", "Single", "2", 0, 15);
        yield return S("SpawnInterval", "Single", "0.15", 0.05, 1);
        yield return S("PauseOnChoice", "Boolean", "true");
    }

    // The third-person add-on's settings (MelonPreferences category "FellSellCharacter").
    static IEnumerable<Setting> CharacterSettings()
    {
        Setting S(string key, string type, string def, double? min = null, double? max = null) =>
            new() { Section = "FellSellCharacter", Key = key, Type = type, Default = def, Value = def, Min = min, Max = max, Description = "" };
        yield return S("ViewKey", "String", "V");
        yield return S("EditorKey", "String", "F7");
        yield return S("Distance", "Single", "2.5", 1, 5);
        yield return S("ShoulderRight", "Single", "0.7", 0, 1.5);
        yield return S("ShoulderUp", "Single", "0.3", -0.3, 1);
        yield return S("Wardrobe", "Boolean", "true");
    }

    // The co-op add-on's player settings (MelonPreferences category "FellSellPlus_Coop"). Role, transport
    // and addresses stay in the cfg: the game's main menu picks host or guest.
    static IEnumerable<Setting> CoopSettings()
    {
        Setting S(string key, string type, string def, double? min = null, double? max = null) =>
            new() { Section = "FellSellPlus_Coop", Key = key, Type = type, Default = def, Value = def, Min = min, Max = max, Description = "" };
        yield return S("MenuButton", "Boolean", "true");
        yield return S("PlayerName", "String", "Player");
        yield return S("FollowIntoRuns", "Boolean", "true");
        yield return S("WaitForPlayers", "Boolean", "true");
        yield return S("NoPause", "Boolean", "true");
        yield return S("LootCut", "Single", "0.3", 0, 0.9);
        yield return S("ReviveRadius", "Single", "3", 1, 10);
        yield return S("ReviveSeconds", "Single", "5", 1, 30);
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

// Add-on mods ship as separate dlls. Off = the dll is parked in UserData/FellSellPlus/DisabledMods,
// because MelonLoader loads every dll in Mods and has no per-mod switch of its own.
public static class Addons
{
    public sealed record Addon(string File, string NameRu, string NameEn, string HintRu, string HintEn, string Requires = null);

    public static readonly Addon[] All =
    {
        new("FellSellBows.dll", "Луки", "Bows",
            "Три лука в левой руке: зажми левую кнопку, чтобы натянуть, отпусти, чтобы выстрелить. Крафтятся, рецепты открыты сразу.",
            "Three bows held in the left hand: hold the left button to draw, release to shoot. Craftable, recipes known from the start."),
        new("FellSellHorde.dll", "Бесконечный режим", "Endless horde",
            "Волны врагов на арене первого босса. Вход у статуи жнеца рядом с калиткой лавки.",
            "Endless waves in the first boss's hall. Enter at the reaper statue by the shop gate."),
        new("FellSellCharacter.dll", "Вид от третьего лица", "Third person",
            "Камера за правым плечом (клавиша V) и редактор облика героя: F7 или гардероб у калитки лавки.",
            "A camera over the right shoulder (V key) and a hero look editor: F7 or the wardrobe by the shop gate."),
        new("FellSellCoop.dll", "Кооператив", "Co-op",
            "Подземелье и лес вдвоём с другом из Steam: враги общие, добыча у каждого своя. В деревне: Esc, «Позвать друга»; друг жмёт «… играет: присоединиться». Ранняя версия.",
            "The dungeon and the forest with a Steam friend: shared enemies, your own loot. In the village: Esc, Invite a friend; your friend clicks \"... is playing: join\". An early version.", Requires: "FellSellCharacter.dll"),
    };

    static string Mods(string game, Addon a) => Path.Combine(game, "Mods", a.File);
    static string Parked(string game, Addon a) => Path.Combine(game, "UserData", "FellSellPlus", "DisabledMods", a.File);

    public static bool Present(string game, Addon a) => game != null && (File.Exists(Mods(game, a)) || File.Exists(Parked(game, a)));
    public static bool On(string game, Addon a) => game != null && File.Exists(Mods(game, a));

    public static void Set(string game, Addon a, bool on)
    {
        // Co-op shows partners with the third-person add-on: switching it on brings that one along.
        if (on && a.Requires != null) { var req = All.FirstOrDefault(x => x.File == a.Requires); if (req != null && !On(game, req)) Set(game, req, true); }
        string from = on ? Parked(game, a) : Mods(game, a), to = on ? Mods(game, a) : Parked(game, a);
        if (!File.Exists(from)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to));
        if (File.Exists(to)) File.Delete(to);
        File.Move(from, to);
    }

    public static bool IsOurs(string file) =>
        file.Equals("FellSellPlus.dll", StringComparison.OrdinalIgnoreCase) || All.Any(a => a.File.Equals(file, StringComparison.OrdinalIgnoreCase));
}

// "Restore the game": removes everything the setup added so the game starts as it shipped. If other
// MelonLoader mods are present, the loader stays so they keep working. Saves are never touched.
public static class Restore
{
    public static bool OtherMods(string game)
    {
        var mods = Path.Combine(game, "Mods");
        return Directory.Exists(mods) && Directory.GetFiles(mods, "*.dll").Any(f => !Addons.IsOurs(Path.GetFileName(f)));
    }

    public static void Run(string game)
    {
        bool keepLoader = OtherMods(game);
        File.Delete(Path.Combine(game, "Mods", "FellSellPlus.dll"));
        foreach (var a in Addons.All) File.Delete(Path.Combine(game, "Mods", a.File));
        foreach (var lnk in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Fell & Sell Configurator.lnk"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Fell & Sell Configurator.lnk"),
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
