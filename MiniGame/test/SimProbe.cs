using System.Text;
using Godot;

namespace FortressRush;

/// <summary>
/// Headless logic probe: runs the real game without rendering and reports game facts
/// (fortress destruction, runner positions/HP) every 5 simulated seconds.
/// `godot --headless --script test/SimProbe.cs`
/// </summary>
public partial class SimProbe : SceneTree
{
    private float _t;
    private int _lastReport = -1;

    public override void _Initialize()
    {
        var ps = GD.Load<PackedScene>("res://scenes/Main.tscn");
        Root.AddChild(ps.Instantiate<Node3D>());
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;
        int slot = (int)(_t / 5f);
        if (slot != _lastReport && _t >= 5f)
        {
            _lastReport = slot;
            Report();
        }
        if (_t >= 100f || (Game.Instance != null && Game.Instance.GameOver))
        {
            Report();
            GD.Print("=== SIM END ===");
            Quit(0);
            return true;
        }
        return false;
    }

    private void Report()
    {
        var g = Game.Instance;
        if (g == null) return;

        var sb = new StringBuilder();
        sb.Append($"[{_t:0}s] blasts={Runner.BlastCount} destroyed: ");
        foreach (var f in g.Fortresses)
            if (f.Destroyed) sb.Append($"{(f.Team == Team.Blue ? "蓝" : "红")}{f.Index} ");

        // lowest-index intact fortress HP for each team
        Fortress bb = null, rr = null;
        foreach (var f in g.Fortresses)
        {
            if (f.Destroyed) continue;
            if (f.Team == Team.Blue && (bb == null || f.Index < bb.Index)) bb = f;
            if (f.Team == Team.Red && (rr == null || f.Index < rr.Index)) rr = f;
        }
        if (bb != null)
        {
            sb.Append($"| 蓝{bb.Index}: {bb.CurrentHp:0}/{bb.TotalHp:0} [");
            foreach (var b in bb.Blocks) sb.Append($"(x={b.GlobalPosition.X:0},z={b.GlobalPosition.Z:0}) ");
            sb.Append("] ");
        }
        if (rr != null)
        {
            sb.Append($"| 红{rr.Index}: {rr.CurrentHp:0}/{rr.TotalHp:0} [");
            foreach (var b in rr.Blocks) sb.Append($"(x={b.GlobalPosition.X:0},z={b.GlobalPosition.Z:0}) ");
            sb.Append("] ");
        }
        sb.Append("| runners:");
        foreach (var r in g.Runners)
        {
            sb.Append($" {(r.Team == Team.Blue ? "蓝" : "红")}{(r.IsPlayer ? "*" : "")}" +
                      $"(x={r.Position.X:0},z={r.Position.Z:0},lane={r.TargetLane},spd={r.Speed:0}" +
                      $"{(r.Dead ? ",DEAD" : "")},cd={r.BlastCdTimer:0.0})");
        }
        GD.Print(sb.ToString());
    }
}
