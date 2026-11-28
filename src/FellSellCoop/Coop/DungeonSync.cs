using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Coop;

// Shared dungeon floor. The generator draws rooms from System.Random(randomSeed); with
// useRandomSeed on it first picks randomSeed = Random.Range(0, 9999999)
// (_GenerateDungeonAsync_d__221.MoveNext, 0x18051BE70).
//
// The host plays as usual and reports the seed and floor of the dungeon it has finished generating.
// A client builds that same floor: entering the dungeon it takes the host's floor number and seed;
// when the host is not in the dungeon yet, the client's generation is held back until it is; and when
// the host moves on to another floor, the client's dungeon is reloaded as that floor.
// Playing on the host's floor is not the client's own progress: a save made there writes the client's
// own floor number and record, and the record is put back when the client is home again (the floor
// number the game resets itself when a run ends).
internal static class DungeonSync
{
    // ---- this player's own dungeon ----
    static IntPtr _genPtr;
    static bool _sawGenerating;
    static float _beganAt;
    static int _builtSeed; // the seed this player's current dungeon was generated with (0 = not known yet)

    // ---- what the host last reported (clients only) ----
    static string _hostScene = "";
    static int _hostSeed, _hostFloor;
    static string _bossRoom = "", _hostBossRoom = ""; // the boss room prefab of this player's floor / of the host's
    static float _hostSeen = -100f;

    // ---- a client's generation held back until the host's seed is known ----
    static CustomDungeonGenerator _pending;
    static Vector3 _holdPos;
    static float _nextFollow;
    static bool _leaving; // the dungeon scene is being reloaded as the host's floor
    static bool _hostFloorBuild; // this player's current floor is generated from the host's seed
    static int _ownFloor = -1, _ownMax; // this client's own counters while the host's are in their place

    // ---- following the host from the village into a run ----
    const float FollowDelay = 5f;
    internal static string HostName = "";
    static string _hostPlace = ""; // the last village, dungeon or forest the host was seen in
    static float _followAt = -1f;
    static string _followScene, _followFrom = "MAIN";

    static bool Follower => CoopSettings.On && !CoopSettings.IsHost && CoopSettings.FollowHost.Value;
    // This client goes along when the host starts a run from the village.
    internal static bool JoinsRuns => Follower && CoopSettings.FollowIntoRuns.Value;

    internal static string FollowLine(bool ru)
    {
        if (_followAt < 0f) return null;
        int left = Mathf.CeilToInt(Mathf.Max(0f, _followAt - Time.unscaledTime));
        bool forest = _followScene == "FOREST";
        string line = _followScene == "MAIN"
            ? (ru ? $"{HostName} возвращается в деревню. Идём следом: {left}" : $"{HostName} is going back to the village. Following in {left}")
            : ru ? $"{HostName} уходит в {(forest ? "лес" : "подземелье")}. Идём следом: {left}"
                 : $"{HostName} is entering the {(forest ? "forest" : "dungeon")}. Following in {left}";
        string hard = DifficultyLine(ru);
        return hard != null ? line + "\n" + hard : line;
    }

    // ---- the host's difficulty, shown to a guest before and while he joins a run ----
    // The enemies are the host's, so their health, speed and numbers follow the host's settings; the
    // damage a guest takes is scaled by his own game.
    static int _hostLevel = -1, _hostTier;
    static string _hostChallenge = "";
    static bool _hostHordeRun;

    internal static byte[] DifficultyPacket()
    {
        var w = CoopModule.Begin(CoopModule.MsgInfo, out var ms);
        var dm = DifficultyManager.Instance;
        var plus = DungeonPlusManager.Instance;
        w.Write((byte)(dm != null ? (int)dm.CurrentDifficulty : 255));
        w.Write(Challenge.ChallengeSettings.Describe() ?? "");
        w.Write((byte)Mathf.Clamp(plus != null ? plus.ActiveTier : 0, 0, 255));
        return ms.ToArray();
    }

    internal static void HostDifficulty(System.IO.BinaryReader r)
    {
        int level = r.ReadByte(); string challenge = r.ReadString(); int tier = r.ReadByte();
        if (level != _hostLevel || challenge != _hostChallenge || tier != _hostTier)
            Plugin.L.LogInfo($"Co-op: the host plays on difficulty {level}{(challenge.Length > 0 ? ", challenge " + challenge : "")}{(tier > 0 ? ", Dungeon+" + tier : "")}.");
        _hostLevel = level; _hostChallenge = challenge; _hostTier = tier;
    }

    internal static string DifficultyLine(bool ru)
    {
        if (!Follower || !HostConnected || _hostLevel < 0) return null;
        string name = null;
        if (_hostLevel != 255)
            try { name = DifficultyManager.GetLocalizedDifficultyName((DifficultyLevel)_hostLevel); } catch { }
        if (string.IsNullOrEmpty(name)) name = _hostLevel == 255 ? "?" : ((DifficultyLevel)_hostLevel).ToString();
        string line = (ru ? "Сложность хоста: " : "Host's difficulty: ") + name;
        if (_hostChallenge.Length > 0) line += (ru ? " · испытание " : " · challenge ") + _hostChallenge;
        if (_hostTier > 0) line += $" · Dungeon+{_hostTier}";
        if (HordeBridge.HostHorde) line += ru ? " · орда" : " · horde";
        return line;
    }

    // The game's own way from the village into a run: what its entrance trigger does when the player
    // walks into it (with the game's own checks: the tutorial lock, the demo lock).
    internal static string EnterRun(string scene)
    {
        var fpc = FirstPersonController.Instance;
        if (fpc == null) return "no player";
        Collider body = fpc.characterController;
        if (scene == "DUNGEON")
        {
            var door = UnityEngine.Object.FindObjectOfType<DungeonEntrance>();
            if (door != null)
            {
                Plugin.L.LogInfo($"Co-op: following the host into the dungeon through {door.name}.");
                door.OnTriggerEnter(body);
                return "entering through " + door.name;
            }
        }
        foreach (var t in UnityEngine.Object.FindObjectsOfType<ForestTeleporter>())
        {
            if (t.sceneName != scene) continue;
            Plugin.L.LogInfo($"Co-op: following the host into {scene} through {t.name}.");
            t.OnTriggerEnter(body);
            return "entering through " + t.name;
        }
        Plugin.L.LogWarning($"Co-op: no entrance to {scene} found here, walk in yourself.");
        return "no entrance to " + scene;
    }
    internal static bool HostConnected => Time.unscaledTime - _hostSeen < 5f;
    internal static bool HostReady => HostConnected && _hostScene == "DUNGEON" && _hostSeed != 0;
    internal static bool Waiting => _pending != null;
    internal static int HostFloor => _hostFloor;
    internal static string HostScene => _hostScene;
    internal static int BuiltSeed => _builtSeed;
    // dev: this client stays behind for a while when the host changes floors (to see the waiting screen)
    internal static void DevDelayFollow(float seconds) => _nextFollow = Time.unscaledTime + seconds;
    internal static string BossRoom => _bossRoom;
    internal static int HostSeed => _hostSeed;
    internal static bool BuildingHostFloor => _hostFloorBuild;
    // This client stands on a finished copy of the floor the host is on.
    internal static bool OnHostFloor => Follower && HostReady && _hostFloorBuild && _builtSeed != 0 && _builtSeed == _hostSeed;

    static void KeepOwnFloor(DungeonManager dm)
    {
        if (_ownFloor >= 0 || dm == null) return;
        _ownFloor = dm.currentFloor; _ownMax = dm.maxFloorReached;
    }

    public static void HostState(string scene, int seed, int floor, string bossRoom)
    {
        if (seed != 0 && (seed != _hostSeed || floor != _hostFloor)) Plugin.L.LogInfo($"Co-op: host dungeon seed {seed} (floor {floor}{(bossRoom.Length > 0 ? ", boss room " + bossRoom : "")}).");
        if (scene is "MAIN" or "DUNGEON" or "FOREST")
        {
            // The host left the village for a run while this player is still there: go along.
            if (_hostPlace == "MAIN" && scene != "MAIN" && JoinsRuns && _followAt < 0f
                && SceneManager.GetActiveScene().name == "MAIN" && FirstPersonController.Instance != null)
            {
                _followAt = Time.unscaledTime + FollowDelay; _followScene = scene; _followFrom = "MAIN";
                Plugin.L.LogInfo($"Co-op: the host entered {scene}, following in {FollowDelay:0} s.");
            }
            // The host walked out of the forest: its enemies went with it, so this player goes home too
            // (the dungeon has its own way out, see Revive.FollowHostHome).
            if (_hostPlace == "FOREST" && scene == "MAIN" && JoinsRuns && _followAt < 0f && !Revive.Down
                && SceneManager.GetActiveScene().name == "FOREST" && FirstPersonController.Instance != null)
            {
                _followAt = Time.unscaledTime + FollowDelay; _followScene = "MAIN"; _followFrom = "FOREST";
                Plugin.L.LogInfo($"Co-op: the host left the forest, following home in {FollowDelay:0} s.");
            }
            _hostPlace = scene;
        }
        _hostScene = scene; _hostSeed = seed; _hostFloor = floor; _hostSeen = Time.unscaledTime; _hostBossRoom = bossRoom;
    }

    // The seed is announced only once the floor is built: before the generator's coroutine has picked it,
    // randomSeed still holds whatever the scene was saved with.
    public static void Current(out int seed, out int floor)
    {
        seed = 0; floor = 0;
        if (SceneManager.GetActiveScene().name != "DUNGEON") return;
        var dm = DungeonManager.Instance;
        if (dm != null) floor = dm.currentFloor;
        var gen = CustomDungeonGenerator.Instance;
        if (gen == null || gen.Pointer != _genPtr || _pending != null || _leaving) return;
        if (gen.isGenerating) { _sawGenerating = true; return; }
        // A floor that built within one frame was never seen generating: give it a moment instead.
        if (!_sawGenerating && Time.unscaledTime - _beganAt < 5f) return;
        seed = gen.randomSeed;
        if (_builtSeed != seed)
        {
            _builtSeed = seed;
            Plugin.L.LogInfo($"Co-op: this dungeon is floor {floor}, seed {seed}.");
        }
    }

    public static void Tick()
    {
        if (!Follower) { _followAt = -1f; return; }
        string scene = SceneManager.GetActiveScene().name;
        bool inDungeon = scene == "DUNGEON";
        if (_followAt >= 0f)
        {
            if (scene != _followFrom || !HostConnected || _hostScene != _followScene || !JoinsRuns || Revive.Down) _followAt = -1f;
            else if (Time.unscaledTime >= _followAt)
            {
                _followAt = -1f;
                try { EnterRun(_followScene); } catch (Exception e) { CoopModule.Warn("following the host into a run", e); }
            }
        }
        if (!inDungeon) _leaving = false;
        if (_ownFloor >= 0 && (scene == "MAIN" || scene == "FOREST") && DungeonManager.Instance != null)
        {
            var own = DungeonManager.Instance;
            Plugin.L.LogInfo($"Co-op: back from the host's dungeon, your own floor record is {_ownMax} as before (it showed {own.maxFloorReached}; your floor there would have been {_ownFloor}).");
            own.maxFloorReached = _ownMax;
            _ownFloor = -1;
        }
        if (_pending != null)
        {
            if (!inDungeon || _pending.WasCollected || _pending.Pointer == IntPtr.Zero) { _pending = null; return; }
            // Nothing is built yet, so there is no floor under the player: keep him where he arrived.
            var fpc = FirstPersonController.Instance;
            if (fpc != null && (fpc.transform.position - _holdPos).sqrMagnitude > 0.01f)
            {
                var cc = fpc.CharacterController;
                cc.enabled = false; fpc.transform.position = _holdPos; cc.enabled = true;
            }
            if (HostReady || !HostConnected)
            {
                var gen = _pending;
                _pending = null;
                Plugin.L.LogInfo(HostReady ? "Co-op: the host is in the dungeon, generating now." : "Co-op: the host is gone, generating your own dungeon.");
                gen.BeginGeneration();
            }
            return;
        }

        // The host went on to another floor (or came into the dungeon after this player built his own).
        if (!inDungeon || !HostReady || _builtSeed == 0 || _hostSeed == _builtSeed || Time.unscaledTime < _nextFollow) return;
        var g = CustomDungeonGenerator.Instance;
        if (g == null || g.isGenerating || g.Pointer != _genPtr) return;
        _nextFollow = Time.unscaledTime + 15f;
        GoToHostFloor();
    }

    // The game changes floors by loading the dungeon scene again; BeginGeneration below then applies
    // the host's floor and seed.
    internal static string GoToHostFloor()
    {
        var menu = UnityEngine.Object.FindObjectOfType<MainPauseMenu>();
        if (menu == null) { Plugin.L.LogWarning("Co-op: cannot follow the host to its floor (no scene loader)."); return "no MainPauseMenu"; }
        Plugin.L.LogInfo($"Co-op: following the host to floor {_hostFloor} (seed {_hostSeed}).");
        _builtSeed = 0; _leaving = true;
        var dm = DungeonManager.Instance;
        if (dm != null) { KeepOwnFloor(dm); dm.currentFloor = _hostFloor; }
        menu.LoadSceneWithLoadingScreen("DUNGEON", true);
        return "loading";
    }

    // BeginGeneration (0x1803EEB00) is called from the generator's OnSceneLoaded and starts the coroutine.
    [HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.BeginGeneration))]
    internal static class UseHostSeedPatch
    {
        static bool Prefix(CustomDungeonGenerator __instance)
        {
            if (!CoopSettings.On) return true;
            _genPtr = __instance.Pointer; _sawGenerating = false; _beganAt = Time.unscaledTime; _builtSeed = 0; _leaving = false;
            _hostFloorBuild = false; _bossRoom = "";
            if (!Follower || !HostConnected) return true; // no host around: a normal solo floor
            var dm = DungeonManager.Instance;
            if (!HostReady)
            {
                _pending = __instance;
                var fpc = FirstPersonController.Instance;
                _holdPos = fpc != null ? fpc.transform.position : Vector3.zero;
                Plugin.L.LogInfo("Co-op: waiting for the host to enter the dungeon before generating.");
                return false;
            }
            int floor = dm != null ? dm.currentFloor : -1;
            if (dm != null && floor != _hostFloor)
            {
                Plugin.L.LogInfo($"Co-op: your floor {floor} becomes the host's floor {_hostFloor}.");
                KeepOwnFloor(dm);
                dm.currentFloor = _hostFloor;
            }
            _hostFloorBuild = true;
            HordeBridge.Arm(HordeBridge.HostHorde);
            __instance.useRandomSeed = false;
            __instance.randomSeed = _hostSeed;
            Plugin.L.LogInfo($"Co-op: generating the host's dungeon (floor {_hostFloor}, seed {_hostSeed}).");
            return true;
        }
    }

    // Which boss room a boss floor gets comes from a shuffle bag that lives outside the seed: the host
    // names its pick and a client building the host's floor takes the same one.
    [HarmonyPatch(typeof(CustomDungeonGenerator), nameof(CustomDungeonGenerator.GetNextBossRoomPrefab))]
    internal static class SameBossRoomPatch
    {
        static void Postfix(CustomDungeonGenerator __instance, ref GameObject __result)
        {
            if (!CoopSettings.On) return;
            try
            {
                if (_hostFloorBuild && _hostBossRoom.Length > 0 && (__result == null || __result.name != _hostBossRoom))
                {
                    var same = FindBossRoom(__instance, _hostBossRoom);
                    if (same != null)
                    {
                        Plugin.L.LogInfo($"Co-op: boss room {(__result != null ? __result.name : "none")} becomes the host's {_hostBossRoom}.");
                        __result = same;
                    }
                    else Plugin.L.LogWarning($"Co-op: the host's boss room {_hostBossRoom} is not known to this game; keeping {(__result != null ? __result.name : "none")}.");
                }
                _bossRoom = __result != null ? __result.name : "";
            }
            catch (Exception e) { CoopModule.Warn("boss room", e); }
        }

        static GameObject FindBossRoom(CustomDungeonGenerator g, string name)
        {
            GameObject In(Il2CppSystem.Collections.Generic.List<GameObject> list)
            {
                for (int i = 0; list != null && i < list.Count; i++) if (list[i] != null && list[i].name == name) return list[i];
                return null;
            }
            var found = In(g.bossRoomPrefabs);
            if (found != null) return found;
            if (g.grandmaBossRoomPrefab != null && g.grandmaBossRoomPrefab.name == name) return g.grandmaBossRoomPrefab;
            for (int i = 0; g.availableBiomes != null && i < g.availableBiomes.Count; i++)
            {
                var b = g.availableBiomes[i];
                if (b == null) continue;
                found = In(b.bossRoomPrefabs);
                if (found != null) return found;
                for (int r = 0; b.bossRooms != null && r < b.bossRooms.Count; r++)
                {
                    var room = b.bossRooms[r]?.roomPrefab;
                    if (room != null && room.name == name) return room;
                }
            }
            return null;
        }
    }

    // A save made while the host's floor number stands in for this client's own writes the client's own.
    [HarmonyPatch(typeof(DungeonManager), nameof(DungeonManager.SaveData))]
    internal static class SaveOwnFloorPatch
    {
        static void Prefix(DungeonManager __instance, out (int floor, int max) __state)
        {
            __state = (-1, 0);
            if (_ownFloor < 0) return;
            __state = (__instance.currentFloor, __instance.maxFloorReached);
            __instance.currentFloor = _ownFloor; __instance.maxFloorReached = _ownMax;
        }

        static void Postfix(DungeonManager __instance, (int floor, int max) __state)
        {
            if (__state.floor < 0) return;
            __instance.currentFloor = __state.floor; __instance.maxFloorReached = __state.max;
        }
    }
}
