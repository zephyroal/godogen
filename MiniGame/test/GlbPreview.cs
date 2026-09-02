using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// GLB model preview tool: renders all .glb files in a directory from multiple
/// angles, measures AABB (via Glb.cs scaler), and saves screenshots + JSON report.
///
/// Usage:
///   godot --script test/GlbPreview.cs -- --dir res://assets/glb [--spin]
///   godot --script test/GlbPreview.cs -- --dir res://assets/glb --height 6
///
/// Without --dir, scans res://assets/glb/.
/// --height sets the target display height (default: auto-detect from filename).
/// --spin slowly rotates the model for video capture.
/// Output: screenshots/preview_<model>_<angle>.png + screenshots/glb_report.json
/// </summary>
public partial class GlbPreview : SceneTree
{
    private Node3D _world;
    private Camera3D _cam;
    private readonly List<(string Path, string Name, float Height)> _models = new();
    private int _idx;
    private int _angle;
    private float _t;
    private string _pendingSnap;
    private bool _spin;
    private float _overrideHeight = 0f;
    private readonly List<Dictionary<string, Variant>> _report = new();

    private static readonly (string Name, Vector3 Pos, Vector3 Target)[] Angles =
    {
        ("front",  new(0, 6, 22),  new(0, 3, 0)),
        ("side",   new(22, 6, 0),  new(0, 3, 0)),
        ("back",   new(0, 6, -22), new(0, 3, 0)),
        ("top",    new(0, 24, 0.1f), new(0, 0, 0)),
    };

    public override void _Initialize()
    {
        string dir = null;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--dir=")) dir = arg.Substring(6);
            if (arg == "--spin") _spin = true;
            if (arg.StartsWith("--height=")) float.TryParse(arg.Substring(9), out _overrideHeight);
        }
        var cmdArgs = OS.GetCmdlineArgs();
        for (int i = 0; i < cmdArgs.Length - 1; i++)
        {
            if (cmdArgs[i] == "--dir" && !cmdArgs[i + 1].StartsWith("--")) dir = cmdArgs[i + 1];
            if (cmdArgs[i] == "--spin") _spin = true;
        }

        if (string.IsNullOrEmpty(dir)) dir = "res://assets/glb";

        var da = DirAccess.Open(dir);
        if (da == null) { GD.PrintErr($"Cannot open: {dir}"); Quit(1); return; }
        da.ListDirBegin();
        string f;
        while ((f = da.GetNext()) != "")
        {
            if (f.EndsWith(".glb"))
            {
                float h = _overrideHeight > 0 ? _overrideHeight : GuessHeight(f);
                _models.Add(($"{dir}/{f}", f.GetBaseName(), h));
            }
        }
        da.ListDirEnd();
        _models.Sort((a, b) => a.Name.CompareTo(b.Name));

        if (_models.Count == 0) { GD.PrintErr($"No .glb files in {dir}"); Quit(1); return; }

        _world = new Node3D { Name = "Preview" };
        Root.AddChild(_world);

        _world.AddChild(new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.16f, 0.18f, 0.22f),
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.5f, 0.55f),
                AmbientLightEnergy = 1.2f,
            },
        });

        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -35f, 0f), LightEnergy = 1.4f, ShadowEnabled = true });
        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30f, 140f, 0f), LightEnergy = 0.3f, LightColor = new Color(0.7f, 0.8f, 1.0f) });

        _world.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(60f, 0.2f, 60f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.35f, 0.30f), Roughness = 1f } },
            Position = new Vector3(0f, -0.1f, 0f),
        });

        Marker(new Color(1f, 0.9f, 0.2f), new Vector3(0, 1f, 12f));
        Marker(new Color(1f, 0.3f, 0.9f), new Vector3(0, 1f, -12f));
        Marker(new Color(0.3f, 0.9f, 1f), new Vector3(12f, 1f, 0f));
        Marker(new Color(1f, 1f, 1f), new Vector3(-12f, 1f, 0f));

        _cam = new Camera3D { Fov = 45f, Near = 0.1f, Far = 300f };
        _world.AddChild(_cam);
        _cam.MakeCurrent();

        GD.Print($"GlbPreview: {_models.Count} models, spin={_spin}");
    }

    private static float GuessHeight(string filename)
    {
        string n = filename.ToLower();
        if (n.Contains("castle")) return 12f;
        if (n.Contains("tower")) return 6f;
        if (n.Contains("tree")) return 5f;
        if (n.Contains("wall")) return 4f;
        if (n.Contains("spawn")) return 4f;
        if (n.Contains("rubble")) return 3f;
        if (n.Contains("character")) return 3f;
        if (n.Contains("bush")) return 2f;
        if (n.Contains("coin")) return 1f;
        return 5f;
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;

        if (_pendingSnap != null)
        {
            var img = Root.GetTexture().GetImage();
            img.SavePng($"res://screenshots/{_pendingSnap}");
            GD.Print($"  saved {_pendingSnap}");
            _pendingSnap = null;
        }

        if (_spin)
        {
            foreach (var child in _world.GetChildren())
                if (child is Node3D n && n.Name == "Model") n.RotateY((float)delta * 0.5f);
            return false;
        }

        for (int i = _world.GetChildCount() - 1; i >= 0; i--)
        {
            var child = _world.GetChild(i);
            if (child is Node3D n && n.Name == "Model") { _world.RemoveChild(child); child.QueueFree(); }
        }

        if (_idx >= _models.Count)
        {
            var arr = new Godot.Collections.Array();
            foreach (var d in _report)
            {
                var gd = new Godot.Collections.Dictionary();
                foreach (var kv in d) gd[kv.Key] = kv.Value;
                arr.Add(gd);
            }
            var json = Json.Stringify(arr, "  ");
            using var file = FileAccess.Open("res://screenshots/glb_report.json", FileAccess.ModeFlags.Write);
            if (file != null) { file.StoreString(json); GD.Print("saved glb_report.json"); }
            GD.Print("=== GLB PREVIEW DONE ===");
            Quit(0);
            return true;
        }

        var (path, name, height) = _models[_idx];

        // use Glb.Create for proper AABB-based scaling (same as in-game)
        var node = Glb.Create(path, height);
        if (node == null) { GD.PrintErr($"  missing: {path}"); _idx++; _angle = 0; return false; }

        node.Name = "Model";
        _world.AddChild(node);

        // measure scaled AABB
        var halfExtents = Glb.ScaledHalfExtents(path, height);
        var size = halfExtents * 2f;

        if (_angle == 0)
        {
            GD.Print($"  {name}: scaled_size=({size.X:F2}, {size.Y:F2}, {size.Z:F2}) height={height}");
            _report.Add(new Dictionary<string, Variant>
            {
                ["name"] = name, ["path"] = path, ["target_height"] = height,
                ["scaled_x"] = size.X, ["scaled_y"] = size.Y, ["scaled_z"] = size.Z,
            });
        }

        var (angleName, camPos, camTarget) = Angles[_angle];
        _cam.Position = camPos;
        _cam.LookAt(camTarget, Vector3.Up);
        _pendingSnap = $"preview_{name}_{angleName}.png";

        _angle++;
        if (_angle >= Angles.Length) { _angle = 0; _idx++; }
        return false;
    }

    private void Marker(Color c, Vector3 pos)
    {
        _world.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.2f, 1.2f, 1.2f), Material = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.5f } },
            Position = pos,
        });
    }
}
