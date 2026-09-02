using Godot;

namespace FortressRush;

/// <summary>
/// Headless network verification: checks NetworkManager state transitions.
/// `godot --headless --script test/NetProbe.cs`
/// </summary>
public partial class NetProbe : SceneTree
{
    private int _pass, _fail;

    public override void _Initialize()
    {
        var ps = GD.Load<PackedScene>("res://scenes/Main.tscn");
        Root.AddChild(ps.Instantiate<Node3D>());
    }

    public override bool _Process(double delta)
    {
        var game = Game.Instance;
        if (game == null || game.Net == null)
        {
            GD.Print("ERROR: Game or Net not ready");
            Quit(1);
            return true;
        }

        var net = game.Net;

        // 1. Initial state
        Check(!net.IsOnline, "Not online at boot");
        Check(!net.IsHost, "Not host at boot");

        // 2. Host creates a server
        var hostErr = net.Host();
        Check(hostErr == Error.Ok, $"Host returns Ok (got {hostErr})");
        Check(net.IsHost, "IsHost after Host()");

        // 3. Close connection
        net.Close();
        Check(!net.IsHost, "Not host after Close()");
        Check(!net.IsOnline, "Not online after Close()");

        // 4. Join with invalid IP
        var joinErr = net.Join("");
        Check(joinErr != Error.Ok, "Join with empty IP fails");
        Check(!net.IsOnline, "Not online after failed join");

        // 5. Game mode transitions
        Check(game.CurrentMode == GameMode.Solo, "Default mode is Solo");

        // 6. Switch to spectator host
        game.ChooseMode(GameMode.SpectatorHost);
        Check(game.CurrentMode == GameMode.SpectatorHost, "Mode set to SpectatorHost");

        // 7. Switch to spectator guest — player input disabled
        game.ChooseMode(GameMode.SpectatorGuest);
        Check(game.CurrentMode == GameMode.SpectatorGuest, "Mode set to SpectatorGuest");
        if (game.Player != null)
            Check(!game.Player.IsPlayer, "Player.IsPlayer = false in SpectatorGuest");

        GD.Print($"=== NET PROBE: {_pass} passed, {_fail} failed ===");
        Quit(_fail == 0 ? 0 : 1);
        return true;
    }

    private void Check(bool ok, string name)
    {
        if (ok) { _pass++; GD.Print($"  PASS {name}"); }
        else { _fail++; GD.Print($"  FAIL {name}"); }
    }
}
