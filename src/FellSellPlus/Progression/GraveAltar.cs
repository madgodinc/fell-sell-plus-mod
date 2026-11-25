using System;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace FellSellPlus.Progression;

// The upgrade spot is a real IInteractable registered into IL2CPP, so the game's own raycast,
// "[E] ..." prompt and interact key drive it. Patching FirstPersonController's interaction code
// instead would mean faking an IInteractable anyway, plus rewriting its prompt logic.
public class GraveAltar : MonoBehaviour
{
    public GraveAltar(IntPtr ptr) : base(ptr) { }

    public void Interact() => ProgressionWindow.Open();
    public string GetActionText() => GameQuery.IsRussian() ? "Почтить память" : "Honour her memory";
    public string GetObjectText() => GameQuery.IsRussian() ? "Наследие бабушки" : "Grandma's Legacy";
}

// Driven from MelonMod.OnUpdate: places the altar whenever the village scene with Grandma's grave
// is loaded and ticks the window.
internal static class ProgressionDriver
{
    static GameObject _altar;
    static float _nextScan;
    static bool _missingLogged;

    public static void Tick()
    {
        if (!ProgressionModule.Active) return;
        ProgressionWindow.Tick();
        if (_altar != null || Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 1f;
        if (FirstPersonController.Instance == null) return; // its interaction layer is needed
        var grave = GameObject.Find("GrandmasGrave");
        if (grave == null) return;
        try { _altar = AltarPlacer.Place(grave); _missingLogged = false; }
        catch (Exception e)
        {
            if (!_missingLogged) Plugin.L.LogError($"Grave altar placement failed: {e}");
            _missingLogged = true;
        }
    }
}

internal static class ProgressionModule
{
    public static bool Active;

    public static void Init()
    {
        if (!ProgressionSettings.Enabled.Value) return;
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<GraveAltar>(new RegisterTypeOptions
            {
                Interfaces = new Il2CppInterfaceCollection(new[] { typeof(IInteractable) }),
            });
            Active = true;
        }
        catch (Exception e) { Plugin.L.LogError($"Progression disabled, type injection failed: {e}"); }
    }
}

internal static class AltarPlacer
{
    // The interaction box wraps the gravestone itself (any side) and reaches eye level, so looking at
    // the grave shows the prompt. The stone is found inside GrandmasGrave by name, nearest to
    // Grandma's letter (it lies at the grave); the whole group's bounds were off to one side.
    public static GameObject Place(GameObject grave)
    {
        var b = StoneBounds(grave);
        float top = Mathf.Max(b.max.y, b.min.y + 2.4f);
        var size = new Vector3(Mathf.Max(b.size.x * 1.15f, 0.7f), top - b.min.y, Mathf.Max(b.size.z * 1.15f, 0.7f));

        var go = new GameObject("FSP_GraveAltar");
        go.transform.position = new Vector3(b.center.x, b.min.y + size.y * 0.5f, b.center.z);
        go.layer = InteractionLayer();
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
        go.AddComponent<GraveAltar>();
        go.transform.SetParent(grave.transform, true);
        Plugin.L.LogInfo($"Grave altar placed at {go.transform.position}, size {size} (layer {go.layer}).");
        return go;
    }

    static Bounds StoneBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        var letter = GameObject.Find("GrandmasLetter");
        // In the village the stone is SM_Prop_Gravestone_05; the grave hole next to it also says
        // "grave" and is closer to the letter, so names rank first: Gravestone, then Tomb, then Grave.
        Renderer best = null; int bestRank = int.MaxValue; float bestDist = float.MaxValue;
        foreach (var r in renderers)
        {
            var n = r.gameObject.name;
            if (n.IndexOf("hole", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            int rank = n.IndexOf("Gravestone", StringComparison.OrdinalIgnoreCase) >= 0 ? 0
                     : n.IndexOf("Tomb", StringComparison.OrdinalIgnoreCase) >= 0 ? 1
                     : n.IndexOf("Grave", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : -1;
            if (rank < 0) continue;
            float d = letter != null ? Vector3.Distance(r.bounds.center, letter.transform.position) : 0f;
            if (rank < bestRank || (rank == bestRank && d < bestDist)) { best = r; bestRank = rank; bestDist = d; }
        }
        if (best != null) { Plugin.L.LogInfo($"Gravestone: {best.gameObject.name}"); return best.bounds; }
        if (renderers.Length == 0) return new Bounds(root.transform.position + Vector3.up * 0.6f, new Vector3(1f, 1.2f, 0.4f));
        var all = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) all.Encapsulate(renderers[i].bounds);
        return all;
    }

    // The player's interaction raycast only sees FirstPersonController.interactionLayer.
    static int InteractionLayer()
    {
        var fpc = FirstPersonController.Instance;
        int mask = fpc != null ? fpc.interactionLayer.value : 0;
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        return 0;
    }
}
