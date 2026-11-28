using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FellSellPlus.Coop;

// Finding each other through Steam instead of typing SteamIDs (Transport = steam, FriendSteamIds empty).
//
// The host opens a friends-only Steam lobby, which marks it as hosting, and lets any Steam friend who
// is in this game open a connection to it. A client looks through its Steam friends for one who is in
// this game and in a lobby, joins that lobby and talks to the lobby's owner.
//
// The game owns Steam's callback pump and throws away the results of calls it did not make itself
// (reading them with ISteamUtils.GetAPICallResult came back empty on both PCs). So nothing here waits
// for a result: the host never needs its lobby's id, and a client sees that it is in by the lobby
// having an owner.
internal static class SteamLobby
{
    const string Lib = "steam_api64";
    const int FriendsOnly = 1, MaxPlayers = 4, ImmediateFriends = 0x04;

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    struct FriendGameInfo { public ulong GameId; public uint Ip; public ushort GamePort, QueryPort; public ulong Lobby; }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamMatchmaking_v009();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamFriends_v018();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamUtils_v010();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamUser_v023();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern uint SteamAPI_ISteamUtils_GetAppID(IntPtr self);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamMatchmaking_CreateLobby(IntPtr self, int type, int maxMembers);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamMatchmaking_JoinLobby(IntPtr self, ulong lobby);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern void SteamAPI_ISteamMatchmaking_LeaveLobby(IntPtr self, ulong lobby);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(IntPtr self, ulong lobby);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyOwner(IntPtr self, ulong lobby);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr self, int flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr self, int index, int flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr self, ulong friend, out FriendGameInfo info);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong friend);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_ISteamFriends_GetFriendPersonaState(IntPtr self, ulong friend);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamMatchmaking_InviteUserToLobby(IntPtr self, ulong lobby, ulong invitee);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamFriends_SetRichPresence(IntPtr self, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    internal struct FriendInfo { public ulong Id, Lobby; public string Name; public bool InGame; }

    static SteamTransport _net;
    static IntPtr _mm, _friends;
    static bool _host, _failed, _created, _ready, _unavailable;
    static ulong _me, _lobby, _joining, _presenceFor;
    static uint _app;
    static float _next, _joinAt;
    static readonly HashSet<ulong> _refused = new();
    static readonly HashSet<ulong> _welcomed = new();

    // A client joins this friend's game, or this lobby (an invitation); both 0 = any friend who hosts.
    internal static ulong WantFriend, WantLobby;

    internal static bool Active => _net != null && !_failed;
    internal static bool InLobby => _host ? _created : _lobby != 0;
    // One line for the status display while nobody is connected yet; null when there is nothing to say.
    internal static string Status(bool ru)
    {
        if (!Active) return null;
        if (_host) return ru ? "Ждём друга (лобби Steam открыто)" : "Waiting for a friend (Steam lobby open)";
        return _lobby == 0 ? (ru ? "Ищем игру друга в Steam..." : "Looking for your friend's game on Steam...") : (ru ? "Подключаемся к другу..." : "Connecting to your friend...");
    }

    // Steam's interfaces, taken once the game has started Steam (it has by the time the main menu shows).
    internal static bool Ready()
    {
        if (_ready) return true;
        if (_unavailable) return false;
        try
        {
            _mm = SteamAPI_SteamMatchmaking_v009(); _friends = SteamAPI_SteamFriends_v018();
            var utils = SteamAPI_SteamUtils_v010(); var user = SteamAPI_SteamUser_v023();
            if (_mm == IntPtr.Zero || _friends == IntPtr.Zero || utils == IntPtr.Zero || user == IntPtr.Zero) return false; // not yet
            _me = SteamAPI_ISteamUser_GetSteamID(user);
            _app = SteamAPI_ISteamUtils_GetAppID(utils);
            _ready = true;
        }
        catch (Exception e) { _unavailable = true; Plugin.L.LogWarning($"Co-op: Steam is unavailable ({e.Message})."); }
        return _ready;
    }

    // The lobby an invitation started this game with (Steam passes "+connect_lobby <id>"); 0 = none.
    internal static ulong LaunchLobby()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id)) return id;
        return 0;
    }

    // Host: the friends-only lobby that marks this player as hosting. Asked for once; Steam makes it a
    // moment later and from then on this player's own "game played" entry carries its id.
    internal static void OpenLobby()
    {
        if (_created || !Ready()) return;
        _created = true;
        SteamAPI_ISteamMatchmaking_CreateLobby(_mm, FriendsOnly, MaxPlayers);
        Plugin.L.LogInfo("Co-op: opening a friends-only Steam lobby.");
    }

    internal static ulong OwnLobby()
    {
        if (!_created || !Ready()) return 0;
        return SteamAPI_ISteamFriends_GetFriendGamePlayed(_friends, _me, out var info) ? info.Lobby : 0;
    }

    internal static void CloseLobby()
    {
        if (!Ready()) return;
        try
        {
            ulong own = OwnLobby();
            if (own != 0) { SteamAPI_ISteamMatchmaking_LeaveLobby(_mm, own); Plugin.L.LogInfo("Co-op: the Steam lobby is closed."); }
            if (_lobby != 0) SteamAPI_ISteamMatchmaking_LeaveLobby(_mm, _lobby);
            if (_presenceFor != 0) SteamAPI_ISteamFriends_SetRichPresence(_friends, "connect", "");
        }
        catch (Exception e) { CoopModule.Warn("closing the lobby", e); }
        _created = false; _lobby = 0; _joining = 0; _presenceFor = 0;
    }

    // A Steam chat invitation to this player's lobby. False when the lobby is not known yet.
    internal static bool Invite(ulong friend)
    {
        ulong own = OwnLobby();
        if (own == 0) return false;
        bool ok = SteamAPI_ISteamMatchmaking_InviteUserToLobby(_mm, own, friend);
        Plugin.L.LogInfo($"Co-op: invitation to {Name(friend)} {(ok ? "sent" : "failed")}.");
        return ok;
    }

    // Steam friends who are online, those in this game first.
    internal static List<FriendInfo> Friends()
    {
        var list = new List<FriendInfo>();
        if (!Ready()) return list;
        int n = SteamAPI_ISteamFriends_GetFriendCount(_friends, ImmediateFriends);
        for (int i = 0; i < n; i++)
        {
            ulong friend = SteamAPI_ISteamFriends_GetFriendByIndex(_friends, i, ImmediateFriends);
            if (friend == 0 || SteamAPI_ISteamFriends_GetFriendPersonaState(_friends, friend) == 0) continue;
            bool playing = SteamAPI_ISteamFriends_GetFriendGamePlayed(_friends, friend, out var info) && (uint)(info.GameId & 0xFFFFFF) == _app;
            list.Add(new FriendInfo { Id = friend, Name = Name(friend), InGame = playing, Lobby = playing ? info.Lobby : 0 });
        }
        list.Sort((a, b) => a.InGame != b.InGame ? (a.InGame ? -1 : 1) : (a.Lobby != 0) != (b.Lobby != 0) ? (a.Lobby != 0 ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static void Start(SteamTransport net, bool host)
    {
        if (!Ready()) { _failed = true; Plugin.L.LogWarning("Co-op: Steam lobby unavailable; list SteamIDs in FriendSteamIds instead."); return; }
        _net = net; _host = host; _failed = false; _joining = 0;
        if (host) _lobby = 0;
        else if (_created) CloseLobby();
        Plugin.L.LogInfo(host
            ? "Co-op: no SteamIDs listed, hosting through a friends-only Steam lobby; friends in this game can join."
            : "Co-op: no SteamIDs listed, looking for a Steam friend who hosts a co-op game.");
    }

    // The session ended: the lobby stays as the main menu left it, only the link is gone.
    internal static void Detach() { _net = null; _welcomed.Clear(); _refused.Clear(); }

    public static void Tick()
    {
        if (!Active || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1.5f;
        try
        {
            if (_host) Host(); else Client();
        }
        catch (Exception e) { _failed = true; Plugin.L.LogWarning($"Co-op: Steam lobby stopped ({e.Message})."); }
    }

    static void Host()
    {
        OpenLobby();
        // Steam's friends list offers "Join game" on this player once the lobby is advertised.
        ulong own = OwnLobby();
        if (own != 0 && _presenceFor != own)
        {
            _presenceFor = own;
            SteamAPI_ISteamFriends_SetRichPresence(_friends, "connect", "+connect_lobby " + own);
            Plugin.L.LogInfo($"Co-op: the Steam lobby is {own}.");
        }
        // A friend becomes a player with his first packet; until then he is only allowed to send one.
        foreach (var f in Friends())
            if (f.InGame && _net.Allow(f.Id) && _welcomed.Add(f.Id)) Plugin.L.LogInfo($"Co-op: Steam friend {f.Name} ({f.Id}) is in the game and may join.");
    }

    static void Client()
    {
        if (_joining != 0)
        {
            // In the lobby once it shows an owner.
            ulong owner = SteamAPI_ISteamMatchmaking_GetLobbyOwner(_mm, _joining);
            if (owner != 0 && owner != _me)
            {
                _lobby = _joining; _joining = 0;
                _net.AddPeer(owner);
                Plugin.L.LogInfo($"Co-op: joined the Steam lobby of {Name(owner)} ({owner}).");
            }
            else if (Time.unscaledTime - _joinAt > 12f)
            {
                Plugin.L.LogWarning($"Co-op: could not join the Steam lobby {_joining}.");
                _refused.Add(_joining); _joining = 0;
                WantLobby = 0; // an invitation that no longer works: any hosting friend will do
            }
            return;
        }
        if (_lobby != 0)
        {
            // The host closed its game: this client is alone in the lobby or owns it now.
            ulong owner = SteamAPI_ISteamMatchmaking_GetLobbyOwner(_mm, _lobby);
            if (owner == 0 || owner == _me || SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(_mm, _lobby) <= 1)
            {
                Plugin.L.LogInfo("Co-op: the host left the Steam lobby, looking for a friend's game again.");
                SteamAPI_ISteamMatchmaking_LeaveLobby(_mm, _lobby);
                _lobby = 0; _refused.Clear();
            }
            return;
        }
        if (WantLobby != 0 && !_refused.Contains(WantLobby))
        {
            Plugin.L.LogInfo($"Co-op: joining the lobby of the invitation ({WantLobby}).");
            _joining = WantLobby; _joinAt = Time.unscaledTime;
            SteamAPI_ISteamMatchmaking_JoinLobby(_mm, WantLobby);
            return;
        }
        foreach (var f in Friends())
        {
            if (f.Lobby == 0 || _refused.Contains(f.Lobby) || (WantFriend != 0 && f.Id != WantFriend)) continue;
            Plugin.L.LogInfo($"Co-op: {f.Name} is in a lobby of this game, joining it.");
            _joining = f.Lobby; _joinAt = Time.unscaledTime;
            SteamAPI_ISteamMatchmaking_JoinLobby(_mm, f.Lobby);
            return;
        }
    }

    static string Name(ulong id)
    {
        try { var p = SteamAPI_ISteamFriends_GetFriendPersonaName(_friends, id); return p != IntPtr.Zero ? Marshal.PtrToStringUTF8(p) : id.ToString(); }
        catch { return id.ToString(); }
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_ISteamFriends_GetPersonaName(IntPtr self);

    static string _ownName;
    internal static string OwnName()
    {
        if (_ownName != null) return _ownName;
        if (!Ready()) return null;
        try { var p = SteamAPI_ISteamFriends_GetPersonaName(_friends); if (p != IntPtr.Zero) _ownName = Marshal.PtrToStringUTF8(p); } catch { }
        return _ownName;
    }

    public static void Stop()
    {
        try { CloseLobby(); } catch { }
        _net = null;
    }
}
