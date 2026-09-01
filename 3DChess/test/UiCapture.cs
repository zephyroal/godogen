using Godot;

namespace Xiangqi3D;

/// <summary>Capture the start menu and end screen for UI verification.</summary>
public partial class UiCapture : SceneTree
{
    private float _t;

    public override void _Initialize()
    {
        var ps = GD.Load<PackedScene>("res://scenes/Main.tscn");
        Root.AddChild(ps.Instantiate<Node3D>());
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= 2.0f && _t < 2.1f)
        {
            var img = Root.GetTexture().GetImage();
            img.SavePng("res://screenshots/ui_start_menu.png");
            GD.Print("saved ui_start_menu.png");
        }
        if (_t >= 1.0f)
        {
            Quit(0);
            return true;
        }
        return false;
    }
}
