using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader.Utils;

namespace FellSellPlus.Crafting;

// Recipes the player marked "Need". Shared with the inventory/loot modules through
// UserData/FellSellPlus/need.json: {"recipes":["<RecipeData asset name>", ...]}.
internal static class NeedList
{
    sealed class Data { public List<string> recipes { get; set; } = new(); }

    static HashSet<string> _set;
    static DateTime _stamp;

    static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus", "need.json");

    static HashSet<string> Set
    {
        get
        {
            // Re-read when another module changed the file.
            var t = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            if (_set != null && t == _stamp) return _set;
            _stamp = t;
            _set = new HashSet<string>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var r in JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath))?.recipes ?? new List<string>())
                        _set.Add(r);
            }
            catch (Exception e) { Plugin.L.LogWarning($"need.json unreadable: {e.Message}"); }
            return _set;
        }
    }

    public static bool Has(RecipeData r) => r != null && Set.Contains(r.name);

    public static bool Toggle(RecipeData r)
    {
        if (r == null) return false;
        var s = Set;
        bool now = !s.Remove(r.name);
        if (now) s.Add(r.name);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data { recipes = new List<string>(s) }));
            _stamp = File.GetLastWriteTimeUtc(FilePath);
        }
        catch (Exception e) { Plugin.L.LogWarning($"need.json save failed: {e.Message}"); }
        return now;
    }
}
