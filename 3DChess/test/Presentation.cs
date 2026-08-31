using System.Collections.Generic;
using Godot;

namespace Xiangqi3D;

/// <summary>
/// Deterministic presentation capture: drives the REAL input pipeline by injecting mouse clicks
/// (unprojected from world positions), plays a scripted opening vs the AI, and saves key screenshots.
/// `godot --write-movie screenshots/result/frame.png --fixed-fps 30 --quit-after 900 --script test/Presentation.cs`
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
        Root.AddChild(ps.Instantiate<Node3D>());

        // NOTE: Game._Ready has not run yet at _Initialize time — resolve Game.Instance lazily inside each action.
        void At(float time, System.Action action) => _events.Add(new Ev { Time = time, Action = action });
        void ClickWorld(int idx) => Click(Game.Instance.Cam.UnprojectPosition(Board.WorldOf(idx)));
        int Idx(int f, int r) => Position.Idx(f, r);

        // pick the vs-AI mode through the real UI button
        At(0.4f, () =>
        {
            var rect = Game.Instance.Hud.BtnVsAI.GetGlobalRect();
            Click(new Vector2(rect.Position.X + rect.Size.X * 0.5f, rect.Position.Y + rect.Size.Y * 0.5f));
        });

        // 1. 炮二平五 (central cannon opening)
        At(2.0f, () => ClickWorld(Idx(7, 2)));
        At(2.9f, () => Snap("01_select"));
        At(3.4f, () => ClickWorld(Idx(4, 2)));

        // 2. 马2进3
        At(6.4f, () => ClickWorld(Idx(7, 0)));
        At(7.6f, () => ClickWorld(Idx(6, 2)));

        // 3. 炮五进四 — capture the black center soldier over the red soldier screen
        At(10.8f, () => ClickWorld(Idx(4, 2)));
        At(12.0f, () => ClickWorld(Idx(4, 6)));
        At(13.0f, () => Snap("02_capture"));

        // 4. develop the rook
        At(16.5f, () => ClickWorld(Idx(8, 0)));
        At(17.7f, () => ClickWorld(Idx(8, 2)));

        // 5. push the central soldier
        At(21.5f, () => ClickWorld(Idx(4, 3)));
        At(22.7f, () => ClickWorld(Idx(4, 4)));

        At(26.0f, () => Snap("03_midgame"));
        At(28.6f, () => Snap("04_final"));
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;
        var game = Game.Instance;
        if (game != null) game.Yaw = 0.22f * Mathf.Sin(_t * 0.12f); // gentle cinematic sway

        for (int i = 0; i < _events.Count; i++)
        {
            if (_events[i].Fired || _t < _events[i].Time) continue;
            _events[i].Fired = true;
            _events[i].Action();
        }
        return false; // --quit-after handles the exit
    }

    /// <summary>Inject a real mouse click at a screen position — exercises the game's actual input path.</summary>
    private static void Click(Vector2 pos)
    {
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = pos,
        });
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = pos,
        });
    }

    private void Snap(string name)
    {
        var img = Root.GetTexture().GetImage();
        img.SavePng($"res://screenshots/{name}.png");
        GD.Print($"saved screenshot {name}.png");
    }
}
