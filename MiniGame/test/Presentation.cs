using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// Deterministic presentation capture (per the engine guide):
/// `godot --write-movie screenshots/result/frame.png --fixed-fps 30 --quit-after 1500 --script test/Presentation.cs`
/// Loads the real game and drives it from a scripted input timeline — not live keys.
/// </summary>
public partial class Presentation : SceneTree
{
    private class Ev
    {
        public float Time;
        public System.Action Action;
        public bool Fired;
    }

    private readonly List<Ev> _events = new();
    private float _t;

    public override void _Initialize()
    {
        var ps = GD.Load<PackedScene>("res://scenes/Main.tscn");
        var main = ps.Instantiate<Node3D>();
        Root.AddChild(main);
        // The first movie frame renders before _Process: Game._Ready pre-positions the camera.

        void At(float time, System.Action action) => _events.Add(new Ev { Time = time, Action = action });

        // sustained attack on the red line: blast off cooldown, weave lanes
        for (float t = 2f; t <= 74f; t += 2.6f)
        {
            float tt = t;
            At(tt, () => Press("blast"));
        }
        At(1.5f, () => Press("move_right")); // lane 2, at the center divider
        At(3.0f, () => Press("move_right")); // lane 3 — cross onto the red line
        At(5.5f, () => Press("move_right")); // lane 4 — deep in red territory
        At(9.0f, () => Press("move_left"));
        At(12.5f, () => Press("dash"));
        At(15.0f, () => Press("move_right"));
        At(18.0f, () => Press("move_left"));
        At(21.0f, () => Press("move_left"));
        At(24.0f, () => Press("dash"));
        At(27.0f, () => Press("move_right"));
        At(30.0f, () => Press("move_left"));
        At(33.0f, () => Press("move_right"));
        At(36.0f, () => Press("move_left"));
        At(2.0f, () => Snap("01_start"));
        At(20.0f, () => Snap("02_midgame"));
        At(34.0f, () => Snap("03_deep_attack"));
        // turn back: home defense alongside the interception AI
        At(39.5f, () => Press("turn_back"));
        At(39.6f, () => Snap("04_defense_turn"));
        At(41.5f, () => Press("move_left"));
        At(43.0f, () => Press("move_left"));
        At(47.5f, () => Press("move_right"));
        At(51.0f, () => Press("move_left"));
        // push the red main city to close out the match
        At(55.5f, () => Press("turn_back")); // face north again
        At(57.5f, () => Press("dash"));
        At(59.0f, () => Press("move_right"));
        At(61.0f, () => Press("move_right"));
        At(63.0f, () => Press("move_right")); // back onto the red line
        At(66.0f, () => Press("move_left"));
        At(68.0f, () => Press("dash"));
        At(70.0f, () => Press("move_right"));
        At(72.5f, () => Snap("05_final"));
        At(78.0f, () => Snap("06_end"));
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;
        for (int i = 0; i < _events.Count; i++)
        {
            if (_events[i].Fired || _t < _events[i].Time) continue;
            _events[i].Fired = true;
            _events[i].Action();
        }
        return false; // false = keep running; --quit-after handles the exit
    }

    private static void Press(string action)
    {
        Input.ActionPress(action);
        Input.ActionRelease(action);
    }

    private void Snap(string name)
    {
        var img = Root.GetTexture().GetImage();
        img.SavePng($"res://screenshots/{name}.png");
        GD.Print($"saved screenshot {name}.png");
    }
}
