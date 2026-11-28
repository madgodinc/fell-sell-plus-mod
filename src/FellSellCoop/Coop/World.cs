using UnityEngine.SceneManagement;

namespace FellSellPlus.Coop;

// Which shared place a player stands in, for everything the host runs there (enemies, effects, room
// events): a dungeon floor is known by its seed, the forest is one place for everybody.
internal static class World
{
    public const int ForestKey = -7;

    static bool Follower => CoopSettings.On && !CoopSettings.IsHost && CoopSettings.FollowHost.Value;

    // The place this player's own game has finished building (0 = none or not ready).
    public static int Key()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene == "FOREST") return FirstPersonController.Instance != null ? ForestKey : 0;
        DungeonSync.Current(out int seed, out _);
        return seed;
    }

    // This client stands in the same place as the host.
    public static bool ClientShares
    {
        get
        {
            if (!Follower || FirstPersonController.Instance == null) return false;
            string scene = SceneManager.GetActiveScene().name;
            if (scene == "DUNGEON") return DungeonSync.OnHostFloor;
            return scene == "FOREST" && DungeonSync.HostConnected && DungeonSync.HostScene == "FOREST";
        }
    }

    public static int ClientKey => SceneManager.GetActiveScene().name == "FOREST" ? ForestKey : DungeonSync.BuiltSeed;
}
