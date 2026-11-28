using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace FellSellCharacter;

// A full Sidekick body built from a Look: the base model's skeleton with a humanoid avatar, one skinned
// part per slot bound to it by bone name, one material with its own 32x32 colour map.
// It knows nothing about the local player, so co-op can build one per remote player.
public sealed class Puppet
{
    public GameObject Root { get; private set; }
    public Animator Animator { get; private set; }
    public readonly Dictionary<string, Transform> Bones = new();

    sealed class SlotState
    {
        public string Name;
        public SkinnedMeshRenderer Renderer;
        public readonly List<GameObject> Grafted = new();
        public readonly List<string> GraftedNames = new();
    }

    readonly Dictionary<int, SlotState> _slots = new();
    readonly Dictionary<string, (Vector3 pos, Quaternion rot)> _attachRest = new();
    Transform _parts;
    Material _material;
    Texture2D _map;
    Color32[] _basePixels;

    static readonly (int slot, string bone)[] AttachBones =
    {
        (Slot.AttachBack, "backAttach"), (Slot.AttachHipsFront, "hipAttachFront"), (Slot.AttachHipsBack, "hipAttachBack"),
        (Slot.AttachHipsL, "hipAttach_l"), (Slot.AttachHipsR, "hipAttach_r"), (Slot.AttachShoulderL, "shoulderAttach_l"),
        (Slot.AttachShoulderR, "shoulderAttach_r"), (Slot.AttachElbowL, "elbowAttach_l"), (Slot.AttachElbowR, "elbowAttach_r"),
        (Slot.AttachKneeL, "kneeAttach_l"), (Slot.AttachKneeR, "kneeAttach_r"),
    };

    public static Puppet Create(string name)
    {
        Catalog.Load();
        var prefab = Resources.Load<GameObject>("Meshes/SK_BaseModel");
        var avatar = Resources.Load<Avatar>("Meshes/SK_BaseModel");
        // The hero's own material is of no use here: its colour map is baked for the hero's baked meshes,
        // while the parts in Resources keep Sidekick's standard swatch layout.
        var baseMaterial = Resources.Load<Material>("Materials/M_BaseMaterial");
        if (prefab == null || avatar == null || baseMaterial == null) throw new InvalidOperationException("Sidekick base model is not in the game's Resources.");

        var p = new Puppet();
        p.Root = UnityEngine.Object.Instantiate(prefab);
        p.Root.name = name;
        UnityEngine.Object.DontDestroyOnLoad(p.Root);
        // The base model's own meshes are a naked mannequin: only its skeleton is used.
        var doomed = new List<GameObject>();
        var rt = p.Root.transform;
        for (int i = 0; i < rt.childCount; i++)
        {
            var ch = rt.GetChild(i);
            if (ch.GetComponent<SkinnedMeshRenderer>() != null) doomed.Add(ch.gameObject);
        }
        foreach (var go in doomed) UnityEngine.Object.Destroy(go);
        foreach (var t in p.Root.GetComponentsInChildren<Transform>(true))
            if (t != rt && !p.Bones.ContainsKey(t.name)) p.Bones[t.name] = t;
        foreach (var (_, bone) in AttachBones)
            if (p.Bones.TryGetValue(bone, out var b)) p._attachRest[bone] = (b.localPosition, b.localRotation);

        var partsGo = new GameObject("Parts");
        partsGo.transform.SetParent(rt, false);
        p._parts = partsGo.transform;

        p.Animator = p.Root.GetComponent<Animator>();
        if (p.Animator == null) p.Animator = p.Root.AddComponent<Animator>();
        p.Animator.runtimeAnimatorController = null;
        p.Animator.avatar = avatar;
        p.Animator.applyRootMotion = false;
        p.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        try { p.Animator.fireEvents = false; } catch { } // enemy clips carry sound and ability events with no receiver here

        p._material = new Material(baseMaterial) { name = name + "_Material", hideFlags = HideFlags.DontUnloadUnusedAsset };
        if (p._material.HasProperty("_DirtAmount")) p._material.SetFloat("_DirtAmount", 0.08f); // as the game sets it on its own people
        p._basePixels = ReadColorMap();
        p._map = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        {
            name = name + "_ColorMap", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };
        p._map.SetPixels32(p._basePixels);
        p._map.Apply();
        p._material.SetTexture("_ColorMap", p._map);
        return p;
    }

    // Sidekick's default colour map: the starting colours of every swatch.
    static Color32[] _defaultPixels;
    static Color32[] ReadColorMap()
    {
        if (_defaultPixels != null) return (Color32[])_defaultPixels.Clone();
        Color32[] px = null;
        try
        {
            var src = Resources.Load<Texture2D>("Textures/T_ColorMap");
            if (src != null && src.width == 32 && src.height == 32)
            {
                if (src.isReadable) px = (Color32[])src.GetPixels32();
                else
                {
                    var rt = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var prev = RenderTexture.active;
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    var tmp = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                    tmp.ReadPixels(new Rect(0, 0, 32, 32), 0, 0, false);
                    tmp.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    px = (Color32[])tmp.GetPixels32();
                    UnityEngine.Object.Destroy(tmp);
                }
            }
        }
        catch (Exception e) { Plugin.L.Warning("Sidekick colour map could not be read: " + e.Message); }
        if (px == null || px.Length != 1024)
        {
            px = new Color32[1024];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 128, 255);
        }
        _defaultPixels = px;
        return (Color32[])px.Clone();
    }

    public void Apply(Look look)
    {
        for (int slot = 1; slot <= Slot.Count; slot++)
        {
            string want = look.Part(slot);
            if (slot == Slot.Wrap) want = WrapFor(look);
            if (string.IsNullOrEmpty(want)) want = null;
            _slots.TryGetValue(slot, out var cur);
            if (cur != null && cur.Name == want) continue;
            if (cur != null) Clear(slot, cur);
            if (want == null) continue;
            var part = Catalog.Find(want);
            if (part == null) { Plugin.L.Warning($"Unknown part '{want}', slot {slot} left empty."); continue; }
            try { _slots[slot] = Build(part); }
            catch (Exception e) { Plugin.L.Error($"Part {want} failed: {e.Message}"); }
        }
        ApplyShape(look);
        ApplyColors(look);
    }

    // The feminine body needs Synty's chest wrap under a bare base torso.
    static string WrapFor(Look look)
    {
        if (look.Feminine < 50f) return null;
        var torso = Catalog.Find(look.Part(Slot.Torso));
        if (torso == null || !torso.Set.EndsWith("_BASE")) return null;
        foreach (var w in Catalog.Parts(Slot.Wrap)) if (w.Set == torso.Set) return w.Name;
        return null;
    }

    void Clear(int slot, SlotState s)
    {
        if (s.Renderer != null) UnityEngine.Object.Destroy(s.Renderer.gameObject);
        foreach (var n in s.GraftedNames) Bones.Remove(n);
        foreach (var go in s.Grafted) if (go != null) UnityEngine.Object.Destroy(go);
        _slots.Remove(slot);
    }

    SlotState Build(Part part)
    {
        var prefab = Resources.Load<GameObject>(part.Path);
        if (prefab == null) throw new InvalidOperationException("not in Resources");
        var inst = UnityEngine.Object.Instantiate(prefab);
        var state = new SlotState { Name = part.Name };
        try
        {
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null) throw new InvalidOperationException("no skinned mesh");
            var src = smr.bones;
            var dst = new Il2CppReferenceArray<Transform>(src.Length);
            for (int i = 0; i < src.Length; i++) dst[i] = Map(src[i], state);
            smr.bones = dst;
            smr.rootBone = Bones.TryGetValue("root", out var root) ? root : Root.transform;
            smr.transform.SetParent(_parts, false);
            // Parts have two submeshes; every one draws with the character's single material.
            var mats = new Il2CppReferenceArray<Material>(Mathf.Max(1, smr.sharedMaterials.Length));
            for (int i = 0; i < mats.Length; i++) mats[i] = _material;
            smr.sharedMaterials = mats;
            smr.gameObject.SetActive(true);
            smr.enabled = true;
            state.Renderer = smr;
        }
        finally { UnityEngine.Object.Destroy(inst); }
        return state;
    }

    // A part's bone on this skeleton. Cloth, hair and cape bones exist only in the part's own prefab:
    // those are moved over, under the bone their parent maps to.
    Transform Map(Transform t, SlotState state)
    {
        if (t == null) return null;
        if (Bones.TryGetValue(t.name, out var known)) return known;
        var parent = Map(t.parent, state);
        if (parent == null) return Bones.TryGetValue("root", out var root) ? root : Root.transform;
        t.SetParent(parent, false);
        state.Grafted.Add(t.gameObject);
        foreach (var d in t.GetComponentsInChildren<Transform>(true))
            if (!Bones.ContainsKey(d.name)) { Bones[d.name] = d; state.GraftedNames.Add(d.name); }
        return t;
    }

    void ApplyShape(Look look)
    {
        float fem = Mathf.Clamp(look.Feminine, 0f, 100f), heavy = Mathf.Clamp(look.Size, 0f, 100f),
              skinny = Mathf.Clamp(-look.Size, 0f, 100f), buff = Mathf.Clamp(look.Muscle, 0f, 100f);
        foreach (var s in _slots.Values)
        {
            var mesh = s.Renderer != null ? s.Renderer.sharedMesh : null;
            if (mesh == null) continue;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string n = mesh.GetBlendShapeName(i);
                if (n.EndsWith("masculineFeminine")) s.Renderer.SetBlendShapeWeight(i, fem);
                else if (n.EndsWith("defaultHeavy")) s.Renderer.SetBlendShapeWeight(i, heavy);
                else if (n.EndsWith("defaultSkinny")) s.Renderer.SetBlendShapeWeight(i, skinny);
                else if (n.EndsWith("defaultBuff")) s.Renderer.SetBlendShapeWeight(i, buff);
            }
        }
        // Attachments are rigid on their bones: the bones follow the body shape by Synty's own offsets.
        float[] w = { fem / 100f, heavy / 100f, skinny / 100f, buff / 100f };
        foreach (var (slot, bone) in AttachBones)
        {
            if (!Bones.TryGetValue(bone, out var b) || !_attachRest.TryGetValue(bone, out var rest)) continue;
            Vector3 pos = Vector3.zero, rot = Vector3.zero;
            for (int k = 0; k < 4; k++)
                if (Catalog.RigMoves.TryGetValue((slot, k), out var mv))
                {
                    pos += mv.pos * w[k];
                    rot += new Vector3(Signed(mv.rot.x), Signed(mv.rot.y), Signed(mv.rot.z)) * w[k];
                }
            b.localPosition = rest.pos + pos;
            b.localRotation = rest.rot * Quaternion.Euler(rot);
        }
    }

    static float Signed(float deg) => deg > 180f ? deg - 360f : deg;

    void ApplyColors(Look look)
    {
        var px = (Color32[])_basePixels.Clone();
        void Cell(int id, Color32 c)
        {
            if (!Catalog.Swatches.TryGetValue(id, out var s)) return;
            int x = s.U * 2, y = s.V * 2;
            if (x < 0 || y < 0 || x > 30 || y > 30) return;
            px[y * 32 + x] = px[y * 32 + x + 1] = px[(y + 1) * 32 + x] = px[(y + 1) * 32 + x + 1] = c;
        }
        Color32 Read(int id)
        {
            if (!Catalog.Swatches.TryGetValue(id, out var s)) return new Color32(128, 128, 128, 255);
            return px[Mathf.Clamp(s.V * 2, 0, 31) * 32 + Mathf.Clamp(s.U * 2, 0, 31)];
        }
        static Color32 Mix(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
        static Color32 Dark(Color32 a, float k) => new((byte)(a.r * k), (byte)(a.g * k), (byte)(a.b * k), 255);

        foreach (var name in new[] { look.Outfit, look.Attachments, look.Materials })
        {
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var p in Catalog.Presets)
                if (p.Name == name && p.Group != 1) { foreach (var kv in p.Colors) Cell(kv.Key, kv.Value); }
        }
        if (Catalog.TryHex(look.Skin, out var skin))
        {
            foreach (int id in new[] { 1, 2, 3, 6, 7, 14 }) Cell(id, skin);            // skin, ears, nose
            foreach (int id in new[] { 4, 8, 9, 10, 11, 12, 13 }) Cell(id, Dark(skin, 0.87f)); // scar, eyelids, nails
            Cell(5, Mix(skin, new Color32(190, 90, 90, 255), 0.3f));                      // lips
        }
        if (Catalog.TryHex(look.Hair, out var hair))
            foreach (int id in new[] { 30, 31, 32, 33, 35, 36 }) Cell(id, hair);        // brows, hair, facial hair
        if (Catalog.TryHex(look.Skin, out _) || Catalog.TryHex(look.Hair, out _))
        {
            var stubble = Mix(Read(1), Read(32), 0.35f);
            Cell(34, stubble); Cell(37, stubble);
        }
        if (Catalog.TryHex(look.Eyes, out var eyes))
        {
            Cell(26, eyes); Cell(27, eyes);
            Cell(22, Dark(eyes, 0.7f)); Cell(23, Dark(eyes, 0.7f));
        }
        _map.SetPixels32(px);
        _map.Apply();
    }

    public Material Material => _material;

    public IEnumerable<SkinnedMeshRenderer> Renderers
    {
        get { foreach (var s in _slots.Values) if (s.Renderer != null) yield return s.Renderer; }
    }

    public SkinnedMeshRenderer RendererOf(int slot) => _slots.TryGetValue(slot, out var s) ? s.Renderer : null;

    public Transform Bone(string name) => Bones.TryGetValue(name, out var t) ? t : null;

    public bool Alive => Root != null;

    public void SetVisible(bool visible)
    {
        if (Root != null && Root.activeSelf != visible) Root.SetActive(visible);
    }

    public void Destroy()
    {
        if (Root != null) UnityEngine.Object.Destroy(Root);
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_map != null) UnityEngine.Object.Destroy(_map);
        Root = null;
    }
}
