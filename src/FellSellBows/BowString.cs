using System;
using UnityEngine;

namespace FellSellBows;

// The drawn string and the nocked arrow. The bow model's own string is part of its solid mesh and
// never bends, so a line is laid from the bow's tips through the nock point, and an arrow model
// sits with its tail on that point. BowDraw computes every point each frame.
internal static class BowString
{
    static GameObject _root, _arrow;
    static LineRenderer _line;
    static int _arrowLong = 2;
    static Vector3 _arrowCenter;
    static float _arrowLen = 0.9f;

    internal static float Width = 0.005f;

    static void Build(Renderer bowRenderer)
    {
        _root = new GameObject("FSB_DrawnString");
        _line = _root.AddComponent<LineRenderer>();
        _line.positionCount = 3;
        _line.useWorldSpace = true;
        _line.numCapVertices = 2;
        var sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var m = new Material(sh) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            _line.sharedMaterial = m;
        }
        else if (bowRenderer != null) _line.sharedMaterial = bowRenderer.sharedMaterial;
        _line.startColor = _line.endColor = new Color(0.10f, 0.08f, 0.06f);
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var mesh = FindMesh("SM_Arrow_01");
        if (mesh != null)
        {
            _arrow = new GameObject("FSB_NockedArrow");
            _arrow.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = _arrow.AddComponent<MeshRenderer>();
            if (bowRenderer != null) mr.sharedMaterial = bowRenderer.sharedMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var s = mesh.bounds.size;
            _arrowLong = s.z >= s.x && s.z >= s.y ? 2 : s.y >= s.x ? 1 : 0;
            _arrowCenter = mesh.bounds.center;
            _arrowLen = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        }
        UnityEngine.Object.DontDestroyOnLoad(_root);
        if (_arrow != null) UnityEngine.Object.DontDestroyOnLoad(_arrow);
    }

    static Mesh FindMesh(string name)
    {
        foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) if (m != null && m.name == name) return m;
        return null;
    }

    // The string runs top - mid - bottom. The arrow (when shown) has its tail at `tail` and points along `dir`.
    internal static void Show(Renderer bowRenderer, Vector3 top, Vector3 mid, Vector3 bottom, bool arrow, Vector3 tail, Vector3 dir, float arrowScale, bool flip)
    {
        if (_root == null) Build(bowRenderer);
        _root.SetActive(true);
        _line.widthMultiplier = Width;
        _line.SetPosition(0, top);
        _line.SetPosition(1, mid);
        _line.SetPosition(2, bottom);
        if (_arrow == null) return;
        _arrow.SetActive(arrow);
        if (!arrow) return;
        Vector3 axis = _arrowLong == 2 ? Vector3.forward : _arrowLong == 1 ? Vector3.up : Vector3.right;
        if (flip) axis = -axis;
        _arrow.transform.localScale = Vector3.one * arrowScale;
        _arrow.transform.rotation = Quaternion.FromToRotation(axis, dir);
        _arrow.transform.position = tail + dir * (_arrowLen * arrowScale * 0.5f) - _arrow.transform.rotation * (_arrowCenter * arrowScale);
    }

    internal static void Hide()
    {
        if (_root != null) _root.SetActive(false);
        if (_arrow != null) _arrow.SetActive(false);
    }
}
