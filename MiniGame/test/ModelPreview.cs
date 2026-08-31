using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// Temp GLB orientation check: renders all imported props at game scale from four
/// cardinal cameras (axis markers: yellow +Z / magenta -Z / cyan +X / white -X).
/// `godot --script test/ModelPreview.cs` — windowed, self-quits, PNGs to screenshots/.
/// </summary>
public partial class ModelPreview : SceneTree
{
    private Camera3D _cam;
    private readonly List<(float Time, string Name, Vector3 Pos)> _shots = new();
    private Vector3? _pendingCam;
    private string _pendingSnap;
    private int _i;
    private float _t;

    public override void _Initialize()
    {
        var world = new Node3D { Name = "Preview" };
        Root.AddChild(world);

        world.AddChild(new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.16f, 0.18f, 0.22f),
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.5f, 0.55f),
                AmbientLightEnergy = 1f,
            },
        });
        world.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50f, -35f, 0f),
            LightEnergy = 1.3f,
            ShadowEnabled = true,
        });

        world.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(70f, 0.2f, 70f),
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.4f, 0.35f), Roughness = 1f },
            },
            Position = new Vector3(0f, -0.1f, 0f),
        });

        Marker(world, new Color(1f, 0.9f, 0.2f), new Vector3(0f, 1f, 18f));  // +Z yellow
        Marker(world, new Color(1f, 0.3f, 0.9f), new Vector3(0f, 1f, -18f)); // -Z magenta
        Marker(world, new Color(0.3f, 0.9f, 1f), new Vector3(20f, 1f, 0f));  // +X cyan
        Marker(world, new Color(1f, 1f, 1f), new Vector3(-20f, 1f, 0f));     // -X white

        Place(world, "res://assets/glb/castle_blue.glb", 12f, new Vector3(-9f, 0f, 0f));
        Place(world, "res://assets/glb/castle_red.glb", 12f, new Vector3(9f, 0f, 0f));
        Place(world, "res://assets/glb/tower_blue.glb", 6f, new Vector3(0f, 0f, 7f));
        Place(world, "res://assets/glb/tower_red.glb", 6f, new Vector3(-7f, 0f, 7f));
        Place(world, "res://assets/glb/tree.glb", 5f, new Vector3(6f, 0f, 7f));

        _cam = new Camera3D { Fov = 50f, Near = 0.1f, Far = 300f };
        world.AddChild(_cam);
        _cam.MakeCurrent();

        _shots.Add((0.15f, "from_plus_z", new Vector3(0f, 9f, 32f)));
        _shots.Add((0.25f, "from_minus_z", new Vector3(0f, 9f, -32f)));
        _shots.Add((0.35f, "from_plus_x", new Vector3(32f, 9f, 0f)));
        _shots.Add((0.45f, "from_minus_x", new Vector3(-32f, 9f, 0f)));
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;
        if (_pendingSnap != null)
        {
            var img = Root.GetTexture().GetImage();
            img.SavePng($"res://screenshots/preview_{_pendingSnap}.png");
            GD.Print($"saved preview_{_pendingSnap}.png");
            _pendingSnap = null;
        }
        if (_pendingCam != null)
        {
            _cam.Position = _pendingCam.Value;
            _cam.LookAt(new Vector3(0f, 4f, 0f), Vector3.Up);
            _pendingCam = null;
        }
        while (_i < _shots.Count && _t >= _shots[_i].Time)
        {
            _pendingCam = _shots[_i].Pos;
            _pendingSnap = _shots[_i].Name;
            _i++;
        }
        if (_i >= _shots.Count && _pendingSnap == null && _pendingCam == null)
        {
            Quit(0);
            return true;
        }
        return false;
    }

    private static void Place(Node3D parent, string path, float height, Vector3 pos)
    {
        var node = Glb.Create(path, height);
        if (node == null)
        {
            GD.PushError($"missing GLB: {path}");
            return;
        }
        node.Position = pos;
        parent.AddChild(node);
        GD.Print($"{path}: size {Glb.ScaledHalfExtents(path, height) * 2f}");
    }

    private static void Marker(Node3D parent, Color c, Vector3 pos)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = new Vector3(1.6f, 1.6f, 1.6f),
                Material = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.5f },
            },
            Position = pos,
        });
    }
}
