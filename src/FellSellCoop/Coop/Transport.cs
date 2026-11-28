using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace FellSellPlus.Coop;

// A peer is identified by a ulong: a SteamID64, or an IPv4 endpoint packed as ip<<16|port.
internal interface ITransport : IDisposable
{
    void Send(ulong peer, byte[] data);
    // Drains received packets into the list.
    void Poll(List<(ulong peer, byte[] data)> into);
    IEnumerable<ulong> KnownPeers { get; }
}

// LAN / same-PC testing. The host binds the port; clients bind an ephemeral port and talk to the host.
internal sealed class UdpTransport : ITransport
{
    readonly UdpClient _udp;
    readonly HashSet<ulong> _peers = new();

    public UdpTransport(bool host, string hostAddress, int port)
    {
        _udp = host ? new UdpClient(port) : new UdpClient(0);
        _udp.Client.Blocking = false;
        // Windows turns an ICMP "port unreachable" (the other side is not up yet) into an error on the
        // next receive; switching that off (SIO_UDP_CONNRESET) keeps the socket quiet while waiting.
        try { _udp.Client.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); } catch { }
        if (!host) _peers.Add(Pack(new IPEndPoint(IPAddress.Parse(hostAddress), port)));
    }

    public IEnumerable<ulong> KnownPeers => _peers;

    public void Send(ulong peer, byte[] data)
    {
        try { _udp.Send(data, data.Length, Unpack(peer)); }
        catch (SocketException e) { Report("send to " + Describe(peer), e); }
    }

    // Each kind of socket error is logged once: a firewall or a wrong address shows up in the log
    // instead of looking like silence.
    readonly HashSet<SocketError> _reported = new();
    void Report(string what, SocketException e)
    {
        if (_reported.Add(e.SocketErrorCode)) Plugin.L.LogWarning($"Co-op: UDP {what} failed: {e.SocketErrorCode} ({e.Message}).");
    }

    public static string Describe(ulong peer) => Unpack(peer).ToString();

    public void Poll(List<(ulong, byte[])> into)
    {
        while (_udp.Available > 0)
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            byte[] data;
            try { data = _udp.Receive(ref ep); } catch (SocketException e) { Report("receive", e); break; }
            var id = Pack(ep);
            _peers.Add(id);
            into.Add((id, data));
        }
    }

    static ulong Pack(IPEndPoint ep) => ((ulong)(uint)BitConverter.ToInt32(ep.Address.GetAddressBytes(), 0) << 16) | (ushort)ep.Port;
    static IPEndPoint Unpack(ulong v) => new(new IPAddress(BitConverter.GetBytes((uint)(v >> 16))), (int)(v & 0xFFFF));

    public void Dispose() => _udp.Dispose();
}

// Internet play through Steam's P2P relay (ISteamNetworking flat API in the game's own
// steam_api64.dll; the game already initialised Steam). Peers are the SteamID64s listed in the cfg
// or, with none listed, found through a Steam lobby (SteamLobby). Sending to a peer opens the
// session; packets from anybody else are dropped.
internal sealed class SteamTransport : ITransport
{
    const string Lib = "steam_api64";
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamNetworking_v006();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ISteamNetworking_SendP2PPacket(IntPtr self, ulong steamId, byte[] data, uint size, int sendType, int channel);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ISteamNetworking_IsP2PPacketAvailable(IntPtr self, out uint size, int channel);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ISteamNetworking_ReadP2PPacket(IntPtr self, byte[] dest, uint cap, out uint size, out ulong steamId, int channel);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(IntPtr self, ulong steamId);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamUser_v023();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);

    const int Unreliable = 1, Channel = 7;
    readonly IntPtr _net;
    readonly HashSet<ulong> _peers = new();
    readonly HashSet<ulong> _allowed = new(); // may connect; a peer from the first packet on

    public SteamTransport(IEnumerable<ulong> friends)
    {
        _net = SteamAPI_SteamNetworking_v006();
        if (_net == IntPtr.Zero) throw new InvalidOperationException("Steam networking unavailable (is Steam running?)");
        foreach (var f in friends) { _peers.Add(f); SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(_net, f); }
        var user = SteamAPI_SteamUser_v023();
        if (user != IntPtr.Zero)
            Plugin.L.LogInfo($"Co-op: your SteamID64 is {SteamAPI_ISteamUser_GetSteamID(user)} (give it to the other players for FriendSteamIds).");
    }

    public IEnumerable<ulong> KnownPeers => _peers;

    // True when the peer is new.
    public bool AddPeer(ulong id)
    {
        if (!_peers.Add(id)) return false;
        SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(_net, id);
        return true;
    }

    // Lets somebody connect without sending him anything yet. True when he was not allowed before.
    public bool Allow(ulong id)
    {
        if (_peers.Contains(id) || !_allowed.Add(id)) return false;
        SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(_net, id);
        return true;
    }

    public void Send(ulong peer, byte[] data) => SteamAPI_ISteamNetworking_SendP2PPacket(_net, peer, data, (uint)data.Length, Unreliable, Channel);

    public void Poll(List<(ulong, byte[])> into)
    {
        while (SteamAPI_ISteamNetworking_IsP2PPacketAvailable(_net, out var size, Channel))
        {
            var buf = new byte[size];
            if (!SteamAPI_ISteamNetworking_ReadP2PPacket(_net, buf, size, out var got, out var from, Channel)) break;
            if (!_peers.Contains(from))
            {
                if (!_allowed.Remove(from)) continue; // only listed friends and friends let in through the lobby
                _peers.Add(from);
            }
            into.Add((from, buf));
        }
    }

    public void Dispose() { }
}
