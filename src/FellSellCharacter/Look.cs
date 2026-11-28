using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader.Utils;

namespace FellSellCharacter;

// What a character wears: one Sidekick part per slot, body shape, colours. Small enough for a file and
// for a network packet: a co-op peer builds the same puppet from ToBytes/FromBytes.
public sealed class Look
{
    public Dictionary<int, string> Parts { get; set; } = new(); // slot number -> part name; a missing slot is empty
    public float Feminine { get; set; }       // 0..100
    public float Size { get; set; }           // -100 slim .. 100 heavy
    public float Muscle { get; set; } = 50f;  // 0..100
    public string Skin { get; set; } = "";    // RRGGBB, empty = the hero's own colours
    public string Hair { get; set; } = "";
    public string Eyes { get; set; } = "";
    public string Outfit { get; set; } = "";      // Synty colour preset names, empty = default colours
    public string Attachments { get; set; } = "";
    public string Materials { get; set; } = "";

    // The hero as the game ships him.
    public static Look Default()
    {
        var l = new Look();
        void Set(int slot, string name) => l.Parts[slot] = name;
        Set(Slot.Head, "SK_HUMN_BASE_02_01HEAD_HU01");
        Set(Slot.Hair, "SK_HUMN_BASE_08_02HAIR_HU01");
        Set(Slot.EyebrowL, "SK_HUMN_BASE_01_03EBRL_HU01"); Set(Slot.EyebrowR, "SK_HUMN_BASE_01_04EBRR_HU01");
        Set(Slot.EyeL, "SK_HUMN_BASE_01_05EYEL_HU01"); Set(Slot.EyeR, "SK_HUMN_BASE_01_06EYER_HU01");
        Set(Slot.EarL, "SK_HUMN_BASE_01_07EARL_HU01"); Set(Slot.EarR, "SK_HUMN_BASE_01_08EARR_HU01");
        Set(Slot.Torso, "SK_HUMN_BASE_01_10TORS_HU01");
        Set(Slot.ArmUpperL, "SK_HUMN_BASE_01_11AUPL_HU01"); Set(Slot.ArmUpperR, "SK_HUMN_BASE_01_12AUPR_HU01");
        Set(Slot.ArmLowerL, "SK_HUMN_BASE_01_13ALWL_HU01"); Set(Slot.ArmLowerR, "SK_HUMN_BASE_01_14ALWR_HU01");
        Set(Slot.HandL, "SK_HUMN_BASE_01_15HNDL_HU01"); Set(Slot.HandR, "SK_HUMN_BASE_01_16HNDR_HU01");
        Set(Slot.Hips, "SK_HUMN_BASE_01_17HIPS_HU01");
        Set(Slot.LegL, "SK_HUMN_BASE_01_18LEGL_HU01"); Set(Slot.LegR, "SK_HUMN_BASE_01_19LEGR_HU01");
        Set(Slot.FootL, "SK_HUMN_BASE_01_20FOTL_HU01"); Set(Slot.FootR, "SK_HUMN_BASE_01_21FOTR_HU01");
        Set(Slot.Nose, "SK_HUMN_BASE_01_35NOSE_HU01");
        Set(Slot.Teeth, "SK_HUMN_BASE_01_36TETH_HU01"); Set(Slot.Tongue, "SK_HUMN_BASE_01_37TONG_HU01");
        return l;
    }

    public Look Clone() => FromBytes(ToBytes());

    public string Part(int slot) => Parts.TryGetValue(slot, out var n) ? n : null;

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static Look FromJson(string json)
    {
        var l = JsonSerializer.Deserialize<Look>(json);
        if (l == null) return Default();
        l.Parts ??= new();
        return l;
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)1); // format version
        w.Write((byte)Parts.Count);
        foreach (var kv in Parts) { w.Write((byte)kv.Key); w.Write(kv.Value ?? ""); }
        w.Write(Feminine); w.Write(Size); w.Write(Muscle);
        w.Write(Skin ?? ""); w.Write(Hair ?? ""); w.Write(Eyes ?? "");
        w.Write(Outfit ?? ""); w.Write(Attachments ?? ""); w.Write(Materials ?? "");
        return ms.ToArray();
    }

    public static Look FromBytes(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        var l = new Look();
        r.ReadByte();
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) { int slot = r.ReadByte(); l.Parts[slot] = r.ReadString(); }
        l.Feminine = r.ReadSingle(); l.Size = r.ReadSingle(); l.Muscle = r.ReadSingle();
        l.Skin = r.ReadString(); l.Hair = r.ReadString(); l.Eyes = r.ReadString();
        l.Outfit = r.ReadString(); l.Attachments = r.ReadString(); l.Materials = r.ReadString();
        return l;
    }

    // The player's own look lives in the mod's folder, never in the game save.
    static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellCharacter", "look.json");

    public static Look LoadSaved()
    {
        try { if (File.Exists(FilePath)) return FromJson(File.ReadAllText(FilePath)); }
        catch (Exception e) { Plugin.L.Warning("look.json is unreadable, using the default look: " + e.Message); }
        return Default();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, ToJson());
        }
        catch (Exception e) { Plugin.L.Warning("Could not save look.json: " + e.Message); }
    }
}
