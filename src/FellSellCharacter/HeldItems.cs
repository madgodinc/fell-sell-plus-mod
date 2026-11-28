using System;
using System.IO;
using UnityEngine;

namespace FellSellCharacter;

// What the hero holds in each hand, packed for another game to draw on his puppet (co-op): the item's
// name, how the first-person hand holds it (the holder's and the item's local placement, which carry
// over because both skeletons have the same bones) and the finger grip. About 250 bytes with both
// hands full; it changes only when the equipment does.
public static class HeldItems
{
    const byte Version = 1;
    internal const int Joints = 3;

    static IntPtr _fpcPtr;
    static readonly Transform[][] _fingers = new Transform[2][];

    // Null while there is no hero to read (menus, loading).
    public static byte[] Local()
    {
        var fpc = FirstPersonController.Instance;
        var eq = PlayerEquipment.Instance;
        var inv = InventoryManager.Instance;
        if (fpc == null || eq == null || inv == null || !ThirdPerson.Bind(fpc)) return null;
        if (fpc.Pointer != _fpcPtr) { _fpcPtr = fpc.Pointer; _fingers[0] = _fingers[1] = null; }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Version);
        WriteHand(w, eq.currentRightHandItem, eq.rightHandHolder, inv.rightHandSlot?.item, true);
        WriteHand(w, eq.currentLeftHandItem, eq.leftHandHolder, inv.leftHandSlot?.item, false);
        return ms.ToArray();
    }

    static void WriteHand(BinaryWriter w, GameObject src, Transform holder, ItemData item, bool right)
    {
        if (src == null || holder == null || item == null) { w.Write(""); return; }
        w.Write(item.name);
        w.Write(src.activeSelf);
        Write(w, ThirdPerson.TurnFor(src, right));
        Write(w, holder.localPosition); Write(w, holder.localRotation); Write(w, holder.localScale);
        var st = src.transform;
        Write(w, st.localPosition); Write(w, st.localRotation); Write(w, st.localScale);
        var bones = _fingers[right ? 0 : 1] ??= FindFingers(holder.parent, right ? "_r" : "_l");
        foreach (var b in bones)
        {
            // A byte per component is about half a degree: plenty for a grip.
            // All zeros stands for a bone this skeleton lacks.
            var q = b != null ? b.localRotation : default;
            w.Write((sbyte)Mathf.RoundToInt(q.x * 127f)); w.Write((sbyte)Mathf.RoundToInt(q.y * 127f));
            w.Write((sbyte)Mathf.RoundToInt(q.z * 127f)); w.Write((sbyte)Mathf.RoundToInt(q.w * 127f));
        }
    }

    static Transform[] FindFingers(Transform hand, string side)
    {
        var found = new Transform[ThirdPerson.FingerNames.Length * Joints];
        if (hand == null) return found;
        foreach (var t in hand.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            int i = FingerIndex(t.name, side);
            if (i >= 0) found[i] = t;
        }
        return found;
    }

    // "index_02_r" with side "_r" is finger 1, joint 2: slot 1 * Joints + 1.
    static int FingerIndex(string name, string side)
    {
        if (!name.EndsWith(side)) return -1;
        for (int f = 0; f < ThirdPerson.FingerNames.Length; f++)
            for (int j = 1; j <= Joints; j++)
                if (name == ThirdPerson.FingerNames[f] + "_0" + j + side) return f * Joints + (j - 1);
        return -1;
    }

    internal static string FingerBone(int slot, string side) => ThirdPerson.FingerNames[slot / Joints] + "_0" + (slot % Joints + 1) + side;

    static void Write(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    static void Write(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
    static Vector3 V3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    static Quaternion Q(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    internal struct Hand
    {
        public string Item; public bool Active;
        public Vector3 Turn, HolderPos, HolderScale, ItemPos, ItemScale;
        public Quaternion HolderRot, ItemRot;
        public Quaternion[] Fingers;
    }

    internal static bool Read(byte[] data, out Hand right, out Hand left)
    {
        right = left = default;
        if (data == null || data.Length < 3 || data[0] != Version) return false;
        using var r = new BinaryReader(new MemoryStream(data));
        r.ReadByte();
        right = ReadHand(r); left = ReadHand(r);
        return true;
    }

    static Hand ReadHand(BinaryReader r)
    {
        var h = new Hand { Item = r.ReadString() };
        if (h.Item.Length == 0) return h;
        h.Active = r.ReadBoolean();
        h.Turn = V3(r);
        h.HolderPos = V3(r); h.HolderRot = Q(r); h.HolderScale = V3(r);
        h.ItemPos = V3(r); h.ItemRot = Q(r); h.ItemScale = V3(r);
        h.Fingers = new Quaternion[ThirdPerson.FingerNames.Length * Joints];
        for (int i = 0; i < h.Fingers.Length; i++)
        {
            var q = new Quaternion(r.ReadSByte() / 127f, r.ReadSByte() / 127f, r.ReadSByte() / 127f, r.ReadSByte() / 127f);
            h.Fingers[i] = q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f ? default : q.normalized;
        }
        return h;
    }
}

// The held items of one puppet that is not the local hero, built from HeldItems.Local() of its player.
public sealed class PuppetHands
{
    sealed class Side
    {
        public string Item = "", Suffix;
        public GameObject Clone;
        public Transform Holder;
        public Quaternion[] Fingers;
        public Transform[] Bones;
        public bool Missing;
    }

    readonly Side _right = new() { Suffix = "_r" }, _left = new() { Suffix = "_l" };

    public void Apply(Puppet body, byte[] data)
    {
        if (body == null || !body.Alive || !HeldItems.Read(data, out var right, out var left)) return;
        Set(body, _right, right, "hand_r");
        Set(body, _left, left, "hand_l");
    }

    static void Set(Puppet body, Side side, in HeldItems.Hand h, string handBone)
    {
        var hand = body.Bone(handBone);
        if (hand == null) return;
        if (h.Item != side.Item || (h.Item.Length > 0 && side.Clone == null && !side.Missing))
        {
            if (side.Clone != null) UnityEngine.Object.Destroy(side.Clone);
            side.Clone = null; side.Item = h.Item; side.Missing = false;
            if (h.Item.Length > 0)
            {
                if (side.Holder == null)
                {
                    side.Holder = new GameObject("FSC_Held").transform;
                    side.Holder.SetParent(hand, false);
                }
                var prefab = Prefab(h.Item);
                side.Clone = prefab != null ? ThirdPerson.MakeVisualCopy(prefab, side.Holder) : null;
                // An item this game does not know (the other player has a mod this one lacks): empty hand.
                if (side.Clone == null) { side.Missing = true; Plugin.L.Warning($"Held item '{h.Item}' cannot be shown: no such item or prefab here."); }
            }
        }
        side.Fingers = h.Item.Length > 0 ? h.Fingers : null;
        if (side.Clone == null) return;
        var turn = Quaternion.Euler(h.Turn);
        side.Holder.localPosition = turn * h.HolderPos;
        side.Holder.localRotation = turn * h.HolderRot;
        side.Holder.localScale = h.HolderScale;
        var ct = side.Clone.transform;
        ct.localPosition = h.ItemPos; ct.localRotation = h.ItemRot; ct.localScale = h.ItemScale;
        if (side.Clone.activeSelf != h.Active) side.Clone.SetActive(h.Active);
    }

    static GameObject Prefab(string item)
    {
        ItemData data = null;
        try { data = ItemDatabaseUtility.GetItemByName(item); } catch { }
        if (data == null || data.name != item)
        {
            var all = ItemDatabaseUtility._database?.allItems;
            for (int i = 0; all != null && i < all.Count; i++)
                if (all[i] != null && all[i].name == item) { data = all[i]; break; }
        }
        return data != null ? data.prefab : null;
    }

    // Every frame after the animator: its clips pose the fingers open, the grip closes them again.
    public void Pose(Puppet body)
    {
        if (body == null || !body.Alive) return;
        Grip(body, _right); Grip(body, _left);
    }

    static void Grip(Puppet body, Side side)
    {
        if (side.Fingers == null || side.Clone == null) return;
        if (side.Bones == null)
        {
            side.Bones = new Transform[side.Fingers.Length];
            for (int i = 0; i < side.Bones.Length; i++) side.Bones[i] = body.Bone(HeldItems.FingerBone(i, side.Suffix));
        }
        for (int i = 0; i < side.Bones.Length; i++)
        {
            var q = side.Fingers[i];
            if (side.Bones[i] != null && (q.w != 0f || q.x != 0f || q.y != 0f || q.z != 0f)) side.Bones[i].localRotation = q;
        }
    }

    public void Reset()
    {
        foreach (var s in new[] { _right, _left })
        {
            if (s.Clone != null) UnityEngine.Object.Destroy(s.Clone);
            if (s.Holder != null) UnityEngine.Object.Destroy(s.Holder.gameObject);
            s.Clone = null; s.Holder = null; s.Item = ""; s.Fingers = null; s.Bones = null; s.Missing = false;
        }
    }
}
