using System;
using UnityEngine;

namespace FellSellBows;

// The game's item icons are renders of the item's own 3D model on a transparent background,
// laid diagonally. Rendering the bow the same way keeps icon and held model identical.
internal static class IconRender
{
    const int Size = 256;
    const int Layer = 31;

    internal static Sprite Render(Mesh mesh, Material mat, string name)
    {
        GameObject root = null;
        RenderTexture rt = null;
        try
        {
            root = new GameObject("FSB_IconRig");
            root.transform.position = new Vector3(0, -5000, 0);

            var model = new GameObject("model");
            model.layer = Layer;
            model.transform.SetParent(root.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // Long axis diagonal, bottom-left to top-right, like the vanilla weapon icons.
            var s = mesh.bounds.size;
            Vector3 longAxis = s.x >= s.y && s.x >= s.z ? Vector3.right : (s.y >= s.z ? Vector3.up : Vector3.forward);
            model.transform.localRotation = Quaternion.AngleAxis(-35f, Vector3.forward) * Quaternion.FromToRotation(longAxis, Vector3.up);
            model.transform.localPosition = -(model.transform.localRotation * mesh.bounds.center);

            var lightGo = new GameObject("light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.cullingMask = 1 << Layer;

            var camGo = new GameObject("cam");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0, 0, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            float extent = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            cam.orthographicSize = extent * 0.5f * 1.05f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);

            rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;

            tex.name = name + "_Icon";
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name + "_Icon";
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }
        catch (Exception e)
        {
            Plugin.L.Warning($"Icon render for {name} failed, keeping the staff icon: {e.Message}");
            return null;
        }
        finally
        {
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            if (root != null) UnityEngine.Object.Destroy(root);
        }
    }
}
