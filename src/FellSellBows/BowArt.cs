using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FellSellBows;

// Each bow's own look, shipped inside the mod: a model (tools/bow_variants.py: the game's bow
// reshaped, recoloured through the palette texture, with a few extra pieces) and a drawn inventory
// icon (tools/bow_icons.py: a PNG decoded to raw pixels at build time, so nothing is decoded in game).
internal static class BowArt
{
    static readonly Dictionary<string, Mesh> Models = new();
    static readonly Dictionary<string, Sprite> Icons = new();
    // Where the string meets the limbs, per model: x = height of the ends above/below the middle,
    // y = the string's z, both in mesh units.
    static readonly Dictionary<IntPtr, Vector2> Anchors = new();

    static Stream Open(string file) => typeof(BowArt).Assembly.GetManifestResourceStream("FellSellBows." + file);

    // The bow's own model, or null when the mod carries none for it (the plain bow is used then).
    internal static Mesh Model(string art)
    {
        if (string.IsNullOrEmpty(art)) return null;
        if (Models.TryGetValue(art, out var done)) return done;
        Mesh m = null;
        try
        {
            using var st = Open($"bow_{art}.bin");
            if (st != null)
            {
                using var r = new BinaryReader(st);
                m = ReadMesh(r, "FSB_Bow_" + art);
                m.RecalculateBounds();
                Anchors[m.Pointer] = new Vector2(r.ReadSingle(), r.ReadSingle());
            }
        }
        catch (Exception e)
        {
            Plugin.L.Warning($"Bow model '{art}' failed, the plain bow is used: {e.Message}");
            m = null;
        }
        Models[art] = m;
        return m;
    }

    // int vertexCount, int indexCount, positions, normals, uvs, indices.
    internal static Mesh ReadMesh(BinaryReader r, string name)
    {
        int nv = r.ReadInt32(), ni = r.ReadInt32();
        var v = new Vector3[nv]; var n = new Vector3[nv]; var uv = new Vector2[nv]; var idx = new int[ni];
        for (int i = 0; i < nv; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        for (int i = 0; i < nv; i++) n[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        for (int i = 0; i < nv; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
        for (int i = 0; i < ni; i++) idx[i] = r.ReadInt32();
        var m = new Mesh { name = name };
        m.vertices = v; m.normals = n; m.uv = uv; m.triangles = idx;
        m.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return m;
    }

    internal static bool TryStringAnchor(Mesh m, out float tipY, out float stringZ)
    {
        tipY = stringZ = 0f;
        if (m == null || !Anchors.TryGetValue(m.Pointer, out var a)) return false;
        tipY = a.x; stringZ = a.y;
        return true;
    }

    // The drawn icon, or null when the mod carries none (the icon is rendered from the model then).
    // File: int width, int height, then RGBA32 rows from the bottom up.
    internal static Sprite Icon(string art, string name)
    {
        if (string.IsNullOrEmpty(art)) return null;
        if (Icons.TryGetValue(art, out var done)) return done;
        Sprite sprite = null;
        try
        {
            using var st = Open($"icon_{art}.rgba");
            if (st != null)
            {
                using var r = new BinaryReader(st);
                int w = r.ReadInt32(), h = r.ReadInt32();
                byte[] px = r.ReadBytes(w * h * 4);
                if (px.Length != w * h * 4) throw new InvalidDataException("icon data is cut short");
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData((Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>)px);
                tex.Apply(false, false); // stays readable: Sprite.Create outlines the sprite from the pixels
                tex.name = name + "_Icon";
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = name + "_Icon";
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
        }
        catch (Exception e)
        {
            Plugin.L.Warning($"Drawn icon '{art}' failed, the rendered one is used: {e.Message}");
            sprite = null;
        }
        Icons[art] = sprite;
        return sprite;
    }
}
