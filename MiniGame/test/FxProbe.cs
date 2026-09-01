using Godot;
using System.Text;

namespace FortressRush;

/// <summary>Verify FX instantiation: fire a blast and check that LaserFx/BlastFx/HitSparkFx nodes appear in the tree.</summary>
public partial class FxProbe : SceneTree
{
    private float _t;
    private bool _fired;

    public override void _Initialize()
    {
        var ps = GD.Load<PackedScene>("res://scenes/Main.tscn");
        Root.AddChild(ps.Instantiate<Node3D>());
        GD.Print("FxProbe ready");
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;

        if (!_fired && _t > 1f)
        {
            _fired = true;
            var game = Game.Instance;
            var player = game.Player;
            // manually fire blast
            player.TryBlast();

            // also manually fire a HitSparkFx and DashTrailFx
            game.AddFx(new HitSparkFx(player.Position + new Vector3(0f, 1.2f, 0f), Game.ColorOf(Team.Blue)));
            game.AddFx(new DashTrailFx(player.Position, Game.ColorOf(Team.Blue)));

            GD.Print($"t={_t:F2}: TryBlast fired, player at {player.Position}");

            // count FX nodes in the tree
            int laser = 0, blast = 0, spark = 0, trail = 0;
            CountNodes(Root, ref laser, ref blast, ref spark, ref trail);
            GD.Print($"FX nodes: LaserFx={laser} BlastFx={blast} HitSparkFx={spark} DashTrailFx={trail}");
        }

        if (_fired && _t > 3f)
        {
            int laser = 0, blast = 0, spark = 0, trail = 0;
            CountNodes(Root, ref laser, ref blast, ref spark, ref trail);
            GD.Print($"t={_t:F2}: FX nodes after 2s: LaserFx={laser} BlastFx={blast} HitSparkFx={spark} DashTrailFx={trail}");
            GD.Print("=== FX PROBE DONE ===");
            Quit(0);
            return true;
        }
        return false;
    }

    private static void CountNodes(Node node, ref int laser, ref int blast, ref int spark, ref int trail)
    {
        if (node is LaserFx) laser++;
        else if (node is BlastFx) blast++;
        else if (node is HitSparkFx) spark++;
        else if (node is DashTrailFx) trail++;

        foreach (var child in node.GetChildren())
            CountNodes(child, ref laser, ref blast, ref spark, ref trail);
    }
}
