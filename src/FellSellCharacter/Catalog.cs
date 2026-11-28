using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FellSellCharacter;

// Sidekick slot numbers, as in the part names (SK_FANT_KNGT_03_10TORS_HU01 is slot 10).
public static class Slot
{
    public const int Head = 1, Hair = 2, EyebrowL = 3, EyebrowR = 4, EyeL = 5, EyeR = 6, EarL = 7, EarR = 8, FacialHair = 9,
        Torso = 10, ArmUpperL = 11, ArmUpperR = 12, ArmLowerL = 13, ArmLowerR = 14, HandL = 15, HandR = 16, Hips = 17,
        LegL = 18, LegR = 19, FootL = 20, FootR = 21, AttachHead = 22, AttachFace = 23, AttachBack = 24, AttachHipsFront = 25,
        AttachHipsBack = 26, AttachHipsL = 27, AttachHipsR = 28, AttachShoulderL = 29, AttachShoulderR = 30, AttachElbowL = 31,
        AttachElbowR = 32, AttachKneeL = 33, AttachKneeR = 34, Nose = 35, Teeth = 36, Tongue = 37, Wrap = 38;
    public const int Count = 38;
}

public sealed class Part
{
    public int Slot;
    public string Name;   // SK_FANT_KNGT_03_10TORS_HU01
    public string Path;   // Resources path
    public string Set;    // FANT_KNGT
    public int Variant;   // 3
    public string Key => Set + "_" + Variant.ToString("00");
}

public sealed class Swatch { public int Id, Group, U, V; public string Name; }
public sealed class ColorPreset { public int Group; public string Name; public Dictionary<int, Color32> Colors = new(); }

// The part list, colour swatches and Synty's colour presets, embedded from the game's own Sidekick database.
public static class Catalog
{
    public static readonly Dictionary<int, List<Part>> BySlot = new();
    public static readonly Dictionary<string, Part> ByName = new(StringComparer.OrdinalIgnoreCase);
    public static readonly Dictionary<int, Swatch> Swatches = new();
    public static readonly Dictionary<string, Swatch> SwatchByName = new();
    public static readonly List<ColorPreset> Presets = new();
    // (slot, blend type 0 feminine / 1 heavy / 2 skinny / 3 muscle) -> attachment bone offset and rotation at full blend
    public static readonly Dictionary<(int, int), (Vector3 pos, Vector3 rot)> RigMoves = new();
    static bool _loaded;

    static readonly (string set, string en, string ru)[] SetNames =
    {
        ("HUMN_BASE", "Human", "Человек"), ("ELVN_BASE", "Elf", "Эльф"), ("FANT_KNGT", "Knight", "Рыцарь"),
        ("FANT_SORC", "Sorcerer", "Чародей"), ("FANT_VILL", "Villager", "Селянин"), ("ELVN_WARR", "Elven warrior", "Эльф-воин"),
        ("SCFI_CIVL", "Stranger", "Чужак"), ("HORR_VILN", "Villain", "Злодей"),
    };

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        foreach (var line in Lines("parts.txt"))
        {
            var f = line.Split('\t');
            string name = f[1].Substring(f[1].LastIndexOf('/') + 1);
            var seg = name.Split('_'); // SK, FANT, KNGT, 03, 10TORS, HU01
            var p = new Part { Slot = int.Parse(f[0]), Name = name, Path = f[1], Set = seg[1] + "_" + seg[2], Variant = int.Parse(seg[3]) };
            if (!BySlot.TryGetValue(p.Slot, out var list)) BySlot[p.Slot] = list = new List<Part>();
            list.Add(p);
            ByName[name] = p;
        }
        foreach (var list in BySlot.Values)
            list.Sort((a, b) => { int c = SetOrder(a.Set).CompareTo(SetOrder(b.Set)); return c != 0 ? c : a.Variant.CompareTo(b.Variant); });
        foreach (var line in Lines("colors.txt"))
        {
            var f = line.Split('\t');
            if (f[0] == "S")
            {
                var s = new Swatch { Id = int.Parse(f[1]), Group = int.Parse(f[2]), U = int.Parse(f[3]), V = int.Parse(f[4]), Name = f[5] };
                Swatches[s.Id] = s; SwatchByName[s.Name] = s;
            }
            else if (f[0] == "P")
            {
                var p = new ColorPreset { Group = int.Parse(f[1]), Name = f[2] };
                foreach (var kv in f[3].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && TryHex(kv.Substring(eq + 1), out var col)) p.Colors[int.Parse(kv.Substring(0, eq))] = col;
                }
                Presets.Add(p);
            }
        }
        var inv = CultureInfo.InvariantCulture;
        foreach (var line in Lines("rig.txt"))
        {
            var f = line.Split('\t');
            float F(int i) => float.Parse(f[i], inv);
            RigMoves[(int.Parse(f[0]), int.Parse(f[1]))] = (new Vector3(F(2), F(3), F(4)), new Vector3(F(5), F(6), F(7)));
        }
    }

    static IEnumerable<string> Lines(string file)
    {
        var asm = typeof(Catalog).Assembly;
        string res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
        if (res == null) yield break;
        using var r = new StreamReader(asm.GetManifestResourceStream(res));
        string line;
        while ((line = r.ReadLine()) != null) if (line.Length > 0) yield return line;
    }

    static int SetOrder(string set)
    {
        for (int i = 0; i < SetNames.Length; i++) if (SetNames[i].set == set) return i;
        return SetNames.Length;
    }

    public static string SetName(string set, bool ru)
    {
        foreach (var s in SetNames) if (s.set == set) return ru ? s.ru : s.en;
        return set;
    }

    public static string Display(Part p, bool ru) => p == null ? (ru ? "Нет" : "None") : SetName(p.Set, ru) + " " + p.Variant;

    public static List<Part> Parts(int slot) => BySlot.TryGetValue(slot, out var l) ? l : new List<Part>();

    public static Part Find(string name) => name != null && ByName.TryGetValue(name, out var p) ? p : null;

    // The same piece for the other side of the body: SK_X_11AUPL_HU01 -> slot 12's SK_X_12AUPR_HU01.
    public static Part Twin(Part p, int otherSlot)
    {
        if (p == null) return null;
        foreach (var q in Parts(otherSlot)) if (q.Set == p.Set && q.Variant == p.Variant) return q;
        return null;
    }

    public static List<ColorPreset> PresetsOf(int group) => Presets.Where(p => p.Group == group).ToList();

    public static bool TryHex(string hex, out Color32 c)
    {
        c = default;
        if (string.IsNullOrEmpty(hex)) return false;
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        c = new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        return true;
    }

    public static string Hex(Color32 c) => c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
}
