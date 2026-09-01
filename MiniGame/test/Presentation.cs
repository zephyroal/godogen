using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// Deterministic presentation capture (per the engine guide):
/// `godot --write-movie screenshots/result/frame.png --fixed-fps 30 --quit-after 2700 --script test/Presentation.cs`
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

        // sustained blast spam on cooldown for the whole arc
        for (float t = 2f; t <= 76f; t += 2.6f)
        {
            float tt = t;
            At(tt, () => Press("blast"));
        }
        // cross onto the red core lane (lane 4) and hold it — cores sit on the middle lane
        At(1.5f, () => Press("move_right")); // lane 2
        At(3.0f, () => Press("move_right")); // lane 3
        At(5.5f, () => Press("move_right")); // lane 4 — red core lane
        At(8.0f, () => Press("dash"));       // break the first wall
        At(14.0f, () => Press("dash"));
        At(20.0f, () => Press("dash"));
        At(2.0f, () => Snap("01_start"));
        At(20.0f, () => Snap("02_midgame"));
        At(21.4f, () => Press("blast"));        // extra blast timed for screenshot
        At(21.5f, () => Snap("02b_blast_fx"));  // capture ~0.1s after the blast
        At(34.0f, () => Snap("03_deep_attack"));
        At(36.2f, () => Press("blast"));        // extra blast timed for screenshot
        At(36.3f, () => Snap("03b_blast_fx2"));  // capture right after the blast
        // brief home-defense turn (showcases 掉头防守 + the interception AI), then resume
        At(38.0f, () => Press("turn_back"));
        At(38.5f, () => Snap("04_defense_turn"));
        At(41.0f, () => Press("turn_back")); // face north, push the red main city
        At(44.0f, () => Press("dash"));
        At(50.0f, () => Press("dash"));
        At(56.0f, () => Press("dash"));
        At(62.0f, () => Press("dash"));
        At(82.0f, () => Snap("05_final"));
        At(89.0f, () => Snap("06_end"));
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
