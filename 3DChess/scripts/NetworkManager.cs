using System;
using Godot;

namespace Xiangqi3D;

/// <summary>ENet P2P networking wrapper. Host creates a server; join connects as client.
/// Gracefully degrades: if offline, all methods no-op and singleplayer works normally.</summary>
public partial class NetworkManager : Node
{
    public const int DefaultPort = 5005;

    public bool IsOnline => Multiplayer.HasMultiplayerPeer() && Multiplayer.GetPeers().Length > 0;
    public bool IsHost { get; private set; }

    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<long> OnPeerConnected;
    public event Action<long> OnPeerDisconnected;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += (id) => OnPeerConnected?.Invoke(id);
        Multiplayer.PeerDisconnected += (id) => OnPeerDisconnected?.Invoke(id);
    }

    public Error Host(int port = DefaultPort)
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(port, maxClients: 1);
        if (err == Error.Ok)
        {
            Multiplayer.MultiplayerPeer = peer;
            IsHost = true;
            OnConnected?.Invoke();
        }
        return err;
    }

    public Error Join(string ip, int port = DefaultPort)
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(ip, port);
        if (err == Error.Ok)
        {
            Multiplayer.MultiplayerPeer = peer;
            IsHost = false;
        }
        return err;
    }

    public void Close()
    {
        if (Multiplayer.HasMultiplayerPeer())
        {
            Multiplayer.MultiplayerPeer.Close();
            Multiplayer.MultiplayerPeer = null;
        }
        IsHost = false;
        OnDisconnected?.Invoke();
    }
}
