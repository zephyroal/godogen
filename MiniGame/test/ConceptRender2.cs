using Godot;
using System.Collections.Generic;

namespace FortressRush;

/// <summary>Render the 4 models that ConceptRender didn't reach.</summary>
public partial class ConceptRender2 : SceneTree
{
    private Camera3D _cam;
    private readonly List<(string Path, string Name, float Height)> _models = new()
    {
        ("res://assets/glb/spawn_pen_red.glb",  "spawn_pen_red",   4f),
        ("res://assets/glb/rubble.glb",         "rubble",           3f),
        ("res://assets/glb/castle_terminal.glb","castle_terminal", 12f),
        ("res://assets/glb/character.glb",      "character",        3f),
    };
    private int _i;
    private string _pendingSnap;

    public override void _Initialize()
    {
        var world = new Node3D { Name = "Concepts2" };
        Root.AddChild(world);

        world.AddChild(new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.16f, 0.18f, 0.22f),
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.6f, 0.6f, 0.65f),
                AmbientLightEnergy = 1.2f,
            },
        });
        world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -35f, 0f), LightEnergy = 1.4f, ShadowEnabled = true });
        world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30f, 140f, 0f), LightEnergy = 0.3f, LightColor = new Color(0.7f, 0.8f, 1.0f) });
        world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(60f, 0.2f, 60f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.35f, 0.30f), Roughness = 1f } }, Position = new Vector3(0f, -0.1f, 0f) });

        _cam = new Camera3D { Fov = 45f, Near = 0.1f, Far = 300f };
        world.AddChild(_cam);
        _cam.MakeCurrent();
        GD.Print($"ConceptRender2: {_models.Count} models");
    }

    public override bool _Process(double delta)
    {
        if (_pendingSnap != null)
        {
            var img = Root.GetTexture().GetImage();
            img.SavePng($"res://3DModel/concepts/{_pendingSnap}.png");
            GD.Print($"saved {_pendingSnap}.png");
            _pendingSnap = null;
        }

        if (_i < _models.Count)
        {
            var (path, name, height) = _models[_i++];
            var node = Glb.Create(path, height);
            if (node == null) { GD.PushError($"missing: {path}"); return false; }
            Root.GetChild(0).AddChild(node);
            float dist = height * 2.5f;
            _cam.Position = new Vector3(dist * 0.7f, height * 0.9f, dist * 0.7f);
            _cam.LookAt(new Vector3(0, height * 0.35f, 0), Vector3.Up);
            _pendingSnap = name;
        }
        else if (_pendingSnap == null)
        {
            GD.Print("=== DONE ===");
            Quit(0);
            return true;
        }
        return false;
    }
}
