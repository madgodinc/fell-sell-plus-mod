using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader.Utils;
using UnityEngine;

namespace FellSellPlus;

// The configurator shows the game's own icons and location shots. The mod does not ship that art
// (it belongs to the game's developers); instead the running game exports the few pictures it needs
// from its own memory into UserData/FellSellPlus/Art, where the configurator reads them. Pictures
// that are not loaded yet are retried on the next scene load; nothing is redone once a file exists.
internal static class ArtExport
{
    static readonly Dictionary<string, string> Icons = new()
    {
        ["Prayers"] = "Angel Statue_Icon", ["Skills"] = "Big Runestone_Icon", ["Progression"] = "Grandma",
        ["Challenge"] = "UnholySkull_Icon", ["Loot"] = "Golden Chest_Icon", ["LootFilter"] = "Pouch_Icon",
        ["Inventory"] = "Backpack", ["Shop"] = "Orange Shop_Icon", ["Crafting"] = "Workbench_Icon",
        ["Info"] = "Pen and Ink_Icon", ["Console"] = "Book", ["Dev"] = "Book",
        ["preset_vanilla"] = "House", ["preset_recommended"] = "Star", ["preset_easy"] = "Easy",
        ["preset_hard"] = "Hard", ["evil"] = "Reaper Statue_Icon", ["gold"] = "Gold_Icon",
    };

    static readonly Dictionary<string, string> Scenes = new()
    {
        ["Prayers"] = "GoodEvilToughChoice", ["Skills"] = "Skillbooks", ["Progression"] = "Shopping",
        ["Challenge"] = "DungeonPlus", ["Loot"] = "Boss", ["LootFilter"] = "CollectItems",
        ["Inventory"] = "SpecialRooms", ["Shop"] = "IsThePriceRight", ["Crafting"] = "Building",
        ["Info"] = "Queue", ["Console"] = "Damage", ["Dev"] = "Damage", ["Home"] = "Elites",
    };

    static bool _done;

    static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus", "Art");

    public static void OnScene()
    {
        if (_done) return;
        try
        {
            Directory.CreateDirectory(Path.Combine(Dir, "icons"));
            Directory.CreateDirectory(Path.Combine(Dir, "scenes"));
            var want = new Dictionary<string, List<string>>(); // sprite name -> output files
            foreach (var kv in Icons) Want(want, kv.Value, Path.Combine(Dir, "icons", kv.Key + ".png"));
            foreach (var kv in Scenes) Want(want, kv.Value, Path.Combine(Dir, "scenes", kv.Key + ".png"));
            if (want.Count == 0) { _done = true; return; }

            int wrote = 0;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Sprite>()))
            {
                var sp = o?.TryCast<Sprite>();
                if (sp == null || !want.TryGetValue(sp.name, out var outs)) continue;
                var tex = Read(sp);
                if (tex == null) continue;
                // Unity's EncodeToPNG binding fails under Il2CppInterop on this Unity version
                // (BlittableArrayWrapper), so the pixels are read and encoded here.
                byte[] png = Png(tex);
                foreach (var file in outs) { File.WriteAllBytes(file, png); wrote++; }
                UnityEngine.Object.Destroy(tex);
                want.Remove(sp.name);
            }
            if (wrote > 0) Plugin.L.LogInfo($"Configurator art: {wrote} pictures exported, {want.Count} still to find.");
            if (want.Count == 0) _done = true;
        }
        catch (Exception e) { Plugin.L.LogWarning($"Configurator art: {e}"); _done = true; }
    }

    static void Want(Dictionary<string, List<string>> want, string sprite, string file)
    {
        if (File.Exists(file)) return;
        if (!want.TryGetValue(sprite, out var l)) want[sprite] = l = new List<string>();
        l.Add(file);
    }

    // Minimal PNG writer: 8-bit RGBA, no filtering, zlib-compressed. Unity rows run bottom-up.
    static byte[] Png(Texture2D tex)
    {
        int w = tex.width, h = tex.height;
        var px = tex.GetPixels32();
        var raw = new byte[h * (w * 4 + 1)];
        int o = 0;
        for (int y = h - 1; y >= 0; y--)
        {
            raw[o++] = 0;
            for (int x = 0; x < w; x++)
            {
                var c = px[y * w + x];
                raw[o++] = c.r; raw[o++] = c.g; raw[o++] = c.b; raw[o++] = c.a;
            }
        }
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(ms, "IHDR", ihdr);
        using (var z = new MemoryStream())
        {
            using (var zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true)) zs.Write(raw);
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    static void BE(byte[] b, int i, int v) { b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16); b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFFu, t), data) ^ 0xFFFFFFFFu;
        var c = new byte[4]; BE(c, 0, (int)crc); s.Write(c);
    }

    static uint[] _crcTable;
    static uint Crc(uint crc, byte[] data)
    {
        if (_crcTable == null)
        {
            _crcTable = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; _crcTable[n] = c; }
        }
        foreach (var b in data) crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    // Copies a sprite's pixels through a render texture (game textures are not CPU-readable).
    static Texture2D Read(Sprite sp)
    {
        var src = sp.texture;
        if (src == null) return null;
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var r = sp.textureRect;
            // One pixel in from every edge: atlas neighbours bleed into the outer row.
            if (r.width > 8 && r.height > 8) r = new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2);
            var tex = new Texture2D(Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height), TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
            tex.Apply();
            return tex;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
