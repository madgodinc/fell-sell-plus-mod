using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace FellSellCharacter;

// The wardrobe by the shop gate: an armour rack the player can use to open the character editor.
// It is a real IInteractable registered into IL2CPP, so the game's own look-at prompt and E key drive it.
public class Wardrobe : MonoBehaviour
{
    public Wardrobe(IntPtr ptr) : base(ptr) { }
    public void Interact() => Editor.Open();
    public string GetActionText() => Editor.Ru ? "Сменить облик" : "Change your look";
    public string GetObjectText() => Editor.Ru ? "Гардероб" : "Wardrobe";
}

internal static class WardrobePlacer
{
    static bool _registered, _failLogged;
    static GameObject _placed;
    static float _nextTry;

    internal static void Register()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<Wardrobe>(new RegisterTypeOptions
            {
                Interfaces = new Il2CppInterfaceCollection(new[] { typeof(IInteractable) }),
            });
            _registered = true;
        }
        catch (Exception e) { Plugin.L.Error("Wardrobe type injection failed, no wardrobe in the village: " + e.Message); }
    }

    internal static void OnSceneChanged() => _placed = null;

    // Called every frame in the village; places the wardrobe once the scene and the player exist.
    internal static void Tick()
    {
        if (!_registered || !Settings.Wardrobe.Value || _placed != null || Time.unscaledTime < _nextTry) return;
        _nextTry = Time.unscaledTime + 1f;
        if (FirstPersonController.Instance == null) return;
        try { _placed = Place(); _failLogged = false; }
        catch (Exception e) { if (!_failLogged) Plugin.L.Error("Wardrobe placement failed: " + e); _failLogged = true; }
    }

    static GameObject Place()
    {
        if (!TryAnchor(out var pos, out var yaw)) return null;
        var root = new GameObject("FSC_Wardrobe");
        root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));

        ItemData item = null;
        foreach (var n in new[] { "Premium Armor Rack - Blue", "Armor Rack" })
        {
            item = ItemDatabaseUtility.GetItemByName(n);
            if (item != null && item.prefab != null) break;
        }
        if (item?.prefab != null)
        {
            var model = UnityEngine.Object.Instantiate(item.prefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            StripToVisual(model);
        }
        else Plugin.L.Warning("Armour rack item not found: the wardrobe is invisible, only its prompt works.");

        // Interaction box on the layer the player's look ray sees.
        var hit = new GameObject("interact");
        hit.transform.SetParent(root.transform, false);
        hit.layer = InteractionLayer();
        var b = BoundsOf(root);
        hit.transform.position = b.size.sqrMagnitude > 0.01f ? b.center : pos + Vector3.up;
        var box = hit.AddComponent<BoxCollider>();
        box.size = b.size.sqrMagnitude > 0.01f ? Vector3.Max(b.size, new Vector3(0.8f, 1.8f, 0.8f)) : new Vector3(1f, 2f, 1f);
        hit.AddComponent<Wardrobe>();
        Plugin.L.Msg($"Wardrobe placed at {pos} yaw {yaw:0}. To move it, set FellSellCharacter.WardrobePosition = \"x, y, z, yaw\".");
        return root;
    }

    // A furniture prefab carries pickup and placement scripts: keep only what draws, and make it solid.
    static void StripToVisual(GameObject go)
    {
        var kill = new List<Component>();
        foreach (var c in go.GetComponentsInChildren<Component>(true))
        {
            if (c == null || c.TryCast<Transform>() != null || c.TryCast<MeshFilter>() != null || c.TryCast<Renderer>() != null || c.TryCast<LODGroup>() != null) continue;
            var col = c.TryCast<Collider>();
            if (col != null) { col.isTrigger = false; continue; }
            kill.Add(c);
        }
        foreach (var c in kill) UnityEngine.Object.DestroyImmediate(c);
    }

    static Bounds BoundsOf(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return default;
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static bool TryAnchor(out Vector3 pos, out float yaw)
    {
        pos = default; yaw = 0;
        var cfg = Settings.WardrobePosition.Value;
        if (!string.IsNullOrWhiteSpace(cfg))
        {
            var p = cfg.Split(',');
            var inv = CultureInfo.InvariantCulture;
            if (p.Length >= 3 && float.TryParse(p[0], NumberStyles.Float, inv, out var x) && float.TryParse(p[1], NumberStyles.Float, inv, out var y)
                && float.TryParse(p[2], NumberStyles.Float, inv, out var z))
            {
                pos = new Vector3(x, y, z);
                if (p.Length >= 4) float.TryParse(p[3], NumberStyles.Float, inv, out yaw);
                return true;
            }
        }
        // Default: by the fence gate nearest to the shop's main door, one step past the spot of the Horde mod's statue
        // (the gate's other side is a thicket).
        var gate = ShopGate();
        if (gate == null) return false;
        pos = Ground(gate.position + gate.right * 4.4f);
        yaw = gate.eulerAngles.y;
        return true;
    }

    static Transform ShopGate()
    {
        Vector3? shop = null;
        var doors = Door.AllDoors;
        for (int i = 0; doors != null && i < doors.Count; i++)
            if (doors[i] != null && doors[i]._isMainShopDoor) { shop = doors[i].transform.position; break; }
        Transform best = null; float bd = float.MaxValue;
        foreach (var tr in UnityEngine.Object.FindObjectsOfType<Transform>())
        {
            if (!Regex.IsMatch(tr.name, @"^SM_Prop_Meadow_Fence_Gate_Set_\d+$")) continue; // the gate set, not its leaf or latch
            float d = shop.HasValue ? (tr.position - shop.Value).sqrMagnitude : 0f;
            if (d < bd) { bd = d; best = tr; }
        }
        return best;
    }

    // The ground under a point, ignoring the fence itself and triggers.
    static Vector3 Ground(Vector3 p)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; Vector3 at = p;
        foreach (var h in hits)
        {
            var n = h.collider.gameObject.name;
            if (n.Contains("Fence") || n.Contains("Gate")) continue;
            if (h.distance < best) { best = h.distance; at = h.point; }
        }
        return at;
    }

    static int InteractionLayer()
    {
        var fpc = FirstPersonController.Instance;
        int mask = fpc != null ? fpc.interactionLayer.value : 0;
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        return 0;
    }
}
