using System;
using Godot;

namespace FortressRush;

/// <summary>ENet P2P networking wrapper. Host creates a server; join connects as client.
/// Gracefully degrades: if offline, all methods no-op and singleplayer works normally.</summary>
public partial class NetworkManager : Node
{
    public const int DefaultPort = 5006;

    public bool IsOnline => Multiplayer.HasMultiplayerPeer() && Multiplayer.GetPeers().Length > 0;
    public bool IsHost { get; private set; }
    public long LocalPeerId => Multiplayer.GetUniqueId();

    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<long> OnPeerConnected;
    public event Action<long> OnPeerDisconnected;

    private bool _wasOnline;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += HandlePeerConnected;
        Multiplayer.PeerDisconnected += HandlePeerDisconnected;
        Multiplayer.ConnectedToServer += () => OnConnected?.Invoke();
        Multiplayer.ConnectionFailed += HandleConnectionFailed;
        Multiplayer.ServerDisconnected += HandleServerDisconnected;
    }

    public override void _Process(double delta)
    {
        bool online = IsOnline;
        if (_wasOnline && !online)
        {
            _wasOnline = false;
            OnDisconnected?.Invoke();
        }
        _wasOnline = online;
    }

    private void HandlePeerConnected(long id)
    {
        _wasOnline = true;
        OnPeerConnected?.Invoke(id);
    }

    private void HandlePeerDisconnected(long id)
    {
        OnPeerDisconnected?.Invoke(id);
    }

    private void HandleConnectionFailed()
    {
        GD.Print("[Net] 连接失败");
        IsHost = false;
        OnDisconnected?.Invoke();
    }

    private void HandleServerDisconnected()
    {
        GD.Print("[Net] 主机已断开");
        IsHost = false;
        _wasOnline = false;
        OnDisconnected?.Invoke();
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
        if (string.IsNullOrWhiteSpace(ip))
            return Error.InvalidParameter;

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
        _wasOnline = false;
        OnDisconnected?.Invoke();
    }
}
