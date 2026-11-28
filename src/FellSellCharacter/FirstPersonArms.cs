using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace FellSellCharacter;

// The arms the player sees in first person wear the chosen look too: the six arm renderers of the game's
// own first-person body get the chosen parts' meshes and the character's material. The renderers
// themselves stay, so the game (and the Bows mod) keep showing, hiding and posing them as before.
// With the default look everything is put back exactly as the game had it.
internal static class FirstPersonArms
{
    static readonly int[] ArmSlots = { Slot.ArmUpperL, Slot.ArmUpperR, Slot.ArmLowerL, Slot.ArmLowerR, Slot.HandL, Slot.HandR };

    sealed class Arm
    {
        public SkinnedMeshRenderer Renderer;
        public Mesh Mesh;
        public Il2CppReferenceArray<Transform> Bones;
        public Il2CppReferenceArray<Material> Materials;
        public Bounds Bounds;
        public string Applied;
    }

    static readonly Dictionary<int, Arm> Arms = new();
    static readonly Dictionary<string, Transform> Skeleton = new();
    static IntPtr _rootPtr;

    // Nothing the arms show differs from the hero the game ships.
    internal static bool IsDefault(Look look)
    {
        if (look == null) return true;
        var def = Look.Default();
        foreach (int s in ArmSlots) if (look.Part(s) != def.Part(s)) return false;
        return string.IsNullOrEmpty(look.Skin) && look.Feminine == def.Feminine && look.Size == def.Size && look.Muscle == def.Muscle;
    }

    internal static void Apply(Transform fpRoot, Look look, Puppet body)
    {
        if (fpRoot == null || look == null) return;
        if (fpRoot.Pointer != _rootPtr)
        {
            _rootPtr = fpRoot.Pointer;
            Arms.Clear(); Skeleton.Clear();
            Catalog.Load();
            foreach (var t in fpRoot.GetComponentsInChildren<Transform>(true))
                if (t != null && !Skeleton.ContainsKey(t.name)) Skeleton[t.name] = t;
            foreach (var r in fpRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var part = r != null ? Catalog.Find(r.name) : null;
                if (part == null || Array.IndexOf(ArmSlots, part.Slot) < 0 || Arms.ContainsKey(part.Slot)) continue;
                Arms[part.Slot] = new Arm { Renderer = r, Mesh = r.sharedMesh, Bones = r.bones, Materials = r.sharedMaterials, Bounds = r.localBounds };
            }
        }
        bool custom = !IsDefault(look) && body != null && body.Alive;
        Skeleton.TryGetValue("root", out var root);
        foreach (var kv in Arms)
        {
            var arm = kv.Value;
            if (arm.Renderer == null) continue;
            var src = custom ? body.RendererOf(kv.Key) : null;
            if (src == null || src.sharedMesh == null)
            {
                if (arm.Applied == null) continue;
                arm.Renderer.sharedMesh = arm.Mesh; arm.Renderer.bones = arm.Bones;
                arm.Renderer.sharedMaterials = arm.Materials; arm.Renderer.localBounds = arm.Bounds;
                arm.Applied = null;
                continue;
            }
            if (arm.Applied != src.name)
            {
                // Same skeleton, bound by bone name; a bone the first-person rig lacks carries no arm weights.
                var from = src.bones;
                var to = new Il2CppReferenceArray<Transform>(from.Length);
                for (int i = 0; i < from.Length; i++)
                    to[i] = from[i] != null && Skeleton.TryGetValue(from[i].name, out var t) ? t : root;
                arm.Renderer.sharedMesh = src.sharedMesh;
                arm.Renderer.bones = to;
                arm.Renderer.sharedMaterials = src.sharedMaterials;
                arm.Renderer.localBounds = src.localBounds;
                arm.Applied = src.name;
            }
            var mesh = src.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++) arm.Renderer.SetBlendShapeWeight(i, src.GetBlendShapeWeight(i));
        }
    }
}
