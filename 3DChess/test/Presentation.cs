using System.Collections.Generic;
using Godot;

namespace Xiangqi3D;

/// <summary>
/// Deterministic presentation: drives the REAL game flow via direct API calls (ChooseMode + ExecuteMove),
/// which is fully camera-independent. The AI still replies through its normal pipeline, so the footage
/// shows the 思考中 indicator and AI capture animations. A deliberate illegal-target flash is also shown.
/// `godot --write-movie screenshots/result/frame.png --fixed-fps 30 --quit-after 900 --script test/Presentation.cs`
/// </summary>
public partial class Presentation : SceneTree
{
    private class Ev { public float Time; public System.Action Action; public bool Fired; }
    private readonly List<Ev> _events = new();
    private float _t;

    public override void _Initialize()
    {
        var ps = GD.Load<PackedScene>("res://scenes/Main.tscn");
        Root.AddChild(ps.Instantiate<Node3D>());

        void At(float time, System.Action action) => _events.Add(new Ev { Time = time, Action = action });
        int Idx(int f, int r) => Position.Idx(f, r);
        void Red(Move m) => Game.Instance.ExecuteMove(m);

        // start in vs-AI mode, bypassing the selection screen
        At(0.3f, () => { Game.Instance.Hud.HideStart(); Game.Instance.ChooseMode(Game.Mode.VsAI); });

        // 1. 炮二平五 (central cannon)
        At(1.4f, () => Red(new Move(Idx(7, 2), Idx(4, 2))));
        At(2.2f, () => Snap("01_opening"));

        // 2. 马2进3
        At(4.6f, () => Red(new Move(Idx(7, 0), Idx(6, 2))));
        At(5.6f, () => Snap("02_develop"));

        // 3. 炮五进四 — capture the black center soldier (guaranteed capture footage)
        At(8.2f, () => Red(new Move(Idx(4, 2), Idx(4, 6))));
        At(9.2f, () => Snap("03_capture"));

        // 4. deliberate illegal-target feedback: flash an illegal square
        At(11.8f, () => Game.Instance.Board.ShowIllegal(Idx(0, 4)));
        At(12.1f, () => Snap("04_illegal"));

        // 5. develop the rook
        At(14.5f, () => Red(new Move(Idx(8, 0), Idx(8, 2))));

        // 6. advance the central soldier
        At(18.5f, () => Red(new Move(Idx(4, 3), Idx(4, 4))));
        At(19.5f, () => Snap("05_midgame"));

        At(26.0f, () => Snap("06_final"));
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

    private void Snap(string name)
    {
        var img = Root.GetTexture().GetImage();
        img.SavePng($"res://screenshots/{name}.png");
        GD.Print($"saved screenshot {name}.png");
    }
}
