using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// Batch-render all GLB props to concept preview images.
/// Each model is rendered from a 3/4 elevated angle onto a clean background.
/// Output: screenshots/concepts/<model_name>.png
/// `godot --script test/ConceptRender.cs` (windowed, self-quits)
/// </summary>
public partial class ConceptRender : SceneTree
{
    private Camera3D _cam;
    private readonly List<(string Path, string Name, float Height, Vector3 Pos)> _models = new();
    private int _i;
    private float _t;
    private string _pendingSnap;

    public override void _Initialize()
    {
        var world = new Node3D { Name = "Concepts" };
        Root.AddChild(world);

        // clean studio environment
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

        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50f, -35f, 0f),
            LightEnergy = 1.4f,
            ShadowEnabled = true,
        };
        world.AddChild(sun);

        var fill = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-30f, 140f, 0f),
            LightEnergy = 0.3f,
            LightColor = new Color(0.7f, 0.8f, 1.0f),
        };
        world.AddChild(fill);

        // ground plane
        world.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(60f, 0.2f, 60f),
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.35f, 0.30f), Roughness = 1f },
            },
            Position = new Vector3(0f, -0.1f, 0f),
        });

        _cam = new Camera3D { Fov = 45f, Near = 0.1f, Far = 300f };
        world.AddChild(_cam);
        _cam.MakeCurrent();

        // all GLB models to render
        _models.Add(("res://assets/glb/castle_blue.glb",     "castle_blue",     12f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/castle_red.glb",      "castle_red",      12f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/tower_blue.glb",       "tower_blue",       6f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/tower_red.glb",        "tower_red",        6f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/tree.glb",             "tree_pine",        5f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/tree_broadleaf.glb",   "tree_broadleaf",   5f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/bush.glb",             "bush",             2f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/coin.glb",             "coin",             1f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/compound_blue.glb",    "compound_blue",   10f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/compound_red.glb",     "compound_red",    10f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/wall_segment.glb",     "wall_segment",     4f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/spawn_pen_blue.glb",   "spawn_pen_blue",   4f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/spawn_pen_red.glb",    "spawn_pen_red",    4f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/rubble.glb",           "rubble",           3f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/castle_terminal.glb",  "castle_terminal", 12f, new Vector3(0, 0, 0)));
        _models.Add(("res://assets/glb/character.glb",        "character",        3f, new Vector3(0, 0, 0)));

        GD.Print($"ConceptRender: {_models.Count} models to render");
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;

        // save pending screenshot from previous frame
        if (_pendingSnap != null)
        {
            var img = Root.GetTexture().GetImage();
            var path = $"res://3DModel/concepts/{_pendingSnap}.png";
            img.SavePng(path);
            GD.Print($"saved {path}");
            _pendingSnap = null;
        }

        // next model
        if (_i < _models.Count)
        {
            var (path, name, height, pos) = _models[_i];
            _i++;

            // clear previous model children (keep env/cam/ground)
            var world = Root.GetChild(0);
            for (int j = world.GetChildCount() - 1; j >= 0; j--)
            {
                var child = world.GetChild(j);
                if (child is Node3D n && n.Name != "Ground" && n != _cam)
                {
                    // skip lights and environment
                    if (child is DirectionalLight3D or WorldEnvironment) continue;
                    world.RemoveChild(child);
                    child.QueueFree();
                }
            }

            var node = Glb.Create(path, height);
            if (node == null)
            {
                GD.PushError($"missing GLB: {path}");
                return false;
            }
            node.Position = pos;
            world.AddChild(node);

            // position camera at 3/4 angle, auto-distance based on model height
            float dist = height * 2.5f;
            _cam.Position = new Vector3(dist * 0.7f, height * 0.9f, dist * 0.7f);
            _cam.LookAt(new Vector3(0, height * 0.35f, 0), Vector3.Up);

            _pendingSnap = name;
        }
        else if (_pendingSnap == null)
        {
            GD.Print("=== CONCEPT RENDER DONE ===");
            Quit(0);
            return true;
        }
        return false;
    }
}
