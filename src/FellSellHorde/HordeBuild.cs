using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppEmeraldAI;
using UnityEngine;

namespace FellSellHorde;

// Builds the horde location by hand: the first biome's boss room prefab is the whole location
// (entrance stairs with the exit, the hall, and past the arch the room with the exit portal and
// the two prayer statues). Whatever the generator built for this floor is cleared first; the
// navmesh and the kill plane are rebuilt for the new room.
internal static class HordeBuild
{
    internal static GameObject Arena;

    internal enum Step { Wait, Done, Failed }

    static int _clearedFrame = -1;
    internal static void Reset() => _clearedFrame = -1;

    // Two steps, a few frames apart. Unity removes destroyed objects at the end of the frame: a navmesh
    // baked in the same frame as the clearing still has the generated floor in it, and where that floor
    // hung over the hall the hall got holes (seen with seed 6352277: no hall floor found, the altar
    // landed in the statue room and no enemy could spawn).
    internal static Step Build(CustomDungeonGenerator g)
    {
        var arenaPrefab = HordeGen.ArenaPrefab;
        if (arenaPrefab == null) { Plugin.L.Error("Horde build: no boss room prefab in the first biome."); return Step.Failed; }

        if (_clearedFrame < 0)
        {
            foreach (var e in UnityEngine.Object.FindObjectsOfType<EmeraldSystem>()) UnityEngine.Object.Destroy(e.gameObject);
            var old = g.dungeonRoot;
            if (old != null)
                for (int i = 0; i < old.transform.childCount; i++) old.transform.GetChild(i).gameObject.SetActive(false);
            g.ClearDungeon();
            _clearedFrame = Time.frameCount;
            return Step.Wait;
        }
        if (Time.frameCount < _clearedFrame + 3) return Step.Wait;
        _clearedFrame = -1;
        var root = g.dungeonRoot;
        if (root == null) { root = new GameObject("HordeDungeonRoot"); g.dungeonRoot = root; }

        Arena = UnityEngine.Object.Instantiate(arenaPrefab, Vector3.zero, Quaternion.identity, root.transform);
        Arena.name = arenaPrefab.name + "(Clone)";

        try { g.CreateKillPlane(); } catch (Exception ex) { Plugin.L.Warning("Kill plane: " + ex.Message); }
        try
        {
            var nav = g.navMeshSurface;
            if (nav != null) nav.BuildNavMesh();
            else Plugin.L.Warning("Horde build: no NavMeshSurface on the generator; enemies may not move.");
        }
        catch (Exception ex) { Plugin.L.Error("Horde build: navmesh failed: " + ex.Message); }
        return Step.Done;
    }

    internal static Bounds Bounds(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(root.transform.position, new Vector3(20, 8, 20));
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }
}
