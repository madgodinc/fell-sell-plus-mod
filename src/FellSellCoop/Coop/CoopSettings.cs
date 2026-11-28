using System;
using System.Linq;

namespace FellSellPlus.Coop;

// Co-op prototype settings. Command-line flags override the cfg so two game instances on one PC
// can take different roles: --fsp-coop=host|client  --fsp-name=Name  --fsp-port=27777
internal static class CoopSettings
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<string> Role;
    public static ConfigEntry<string> Transport;
    public static ConfigEntry<string> HostAddress;
    public static ConfigEntry<int> Port;
    public static ConfigEntry<string> FriendSteamIds;
    public static ConfigEntry<string> PlayerName;
    public static ConfigEntry<bool> FollowHost;
    public static ConfigEntry<bool> HostEnemies;
    public static ConfigEntry<float> ReviveRadius;
    public static ConfigEntry<bool> NoPause;
    public static ConfigEntry<bool> WaitForPlayers;
    public static ConfigEntry<float> LootCut;
    public static ConfigEntry<bool> FollowIntoRuns;
    public static ConfigEntry<float> ReviveSeconds;
    public static ConfigEntry<bool> MenuButton;
    public static ConfigEntry<bool> ShareDoors, ShareTraps, ShareForestEvent;

    // What the player chose in the main menu's Co-op window for this session: "host", "client", "off"
    // (single player) or null (the cfg decides). A choice wins over the cfg, and a co-op session made
    // there always plays over Steam with a friends lobby.
    public static string Session;

    public static bool IsHost => RoleValue == "host";
    public static string RoleValue => Arg("--fsp-coop=") ?? Session ?? Role.Value.Trim().ToLowerInvariant();
    public static bool On => Arg("--fsp-coop=") != null || (Session != null ? Session != "off" : Enabled.Value);
    public static bool Udp => Session == null && Transport.Value.Trim().ToLowerInvariant() == "udp";
    public static string SteamIds => Session != null ? "" : FriendSteamIds.Value;
    // A player who never set a name is shown under his Steam name.
    public static string Name => Arg("--fsp-name=") ?? (PlayerName.Value == "Player" && !Udp ? SteamLobby.OwnName() ?? "Player" : PlayerName.Value);
    public static int PortValue => int.TryParse(Arg("--fsp-port="), out var p) ? p : Port.Value;

    public static void Bind(ConfigFile cfg)
    {
        const string s = "Coop";
        Enabled = cfg.Bind(s, "Enabled", false, "Co-op prototype (players see each other). Experimental.");
        Role = cfg.Bind(s, "Role", "host", "host or client.");
        Transport = cfg.Bind(s, "Transport", "steam", "steam (play over the internet with Steam friends) or udp (LAN / testing).");
        HostAddress = cfg.Bind(s, "HostAddress", "127.0.0.1", "udp only: the host's IP address.");
        Port = cfg.Bind(s, "Port", 27777, "udp only: port.");
        FriendSteamIds = cfg.Bind(s, "FriendSteamIds", "", "steam only, optional: comma-separated SteamID64 of the other players (host: everyone; client: the host). Leave empty to find each other through Steam friends: the host opens a friends-only lobby and a client joins the lobby of a friend who is in this game.");
        PlayerName = cfg.Bind(s, "PlayerName", "Player", "Name shown above your character for the others.");
        HostEnemies = cfg.Bind(s, "HostEnemies", true, "Set on both sides: dungeon and forest enemies, bosses and room events are run by the host. A client has none of its own, sees and hits the host's, and they hit it back. Off: every player fights his own enemies.");
        ReviveRadius = cfg.Bind(s, "ReviveRadius", 3f, "A player who died in the dungeon waits; a living partner standing this close (metres) to the exit portal circle brings him back next to it.");
        NoPause = cfg.Bind(s, "NoPause", true, "While a partner is connected, opening your menu, inventory or map does not stop the game (your partner's world would freeze with it).");
        WaitForPlayers = cfg.Bind(s, "WaitForPlayers", true, "A dungeon floor starts only when every player in the run has loaded it; until then everyone sees a waiting screen.");
        LootCut = cfg.Bind(s, "LootCut", 0.3f, "Share of loot removed while you play with a partner (0.3 = 30% less from enemies and chests; 0 = off). Each player still gets his own loot.");
        FollowIntoRuns = cfg.Bind(s, "FollowIntoRuns", true, "client only: when the host enters the dungeon or the forest from the village, you are taken along a few seconds later (a line on the screen counts down). Off: walk in yourself.");
        ReviveSeconds = cfg.Bind(s, "ReviveSeconds", 5f, "While the boss room is locked the portal circle is out of reach: a partner standing next to the one who is down for this many seconds brings him back on the spot.");
        MenuButton = cfg.Bind(s, "MenuButton", true, "A Co-op button in the main menu: host a game or join a Steam friend without editing this file.");
        ShareDoors = cfg.Bind(s, "ShareDoors", true, "Set on both sides: a door one player opened stands open for everybody on that floor.");
        ShareTraps = cfg.Bind(s, "ShareTraps", true, "Set on both sides: a pressure-plate trap one player sets off goes off in every player's game, and spinning blades turn in step with the host's. Each player is still hurt only by the traps of his own game.");
        ShareForestEvent = cfg.Bind(s, "ShareForestEvent", true, "A client's forest takes the host's event (weather, light, what grows and spawns there). Off: every player keeps his own forest event.");
        FollowHost = cfg.Bind(s, "FollowHost", true, "client only: play the host's dungeon floor (wait for the host at the dungeon entrance, move to its floor when it changes floors).");
    }

    static string Arg(string prefix) =>
        Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?.Substring(prefix.Length);
}
