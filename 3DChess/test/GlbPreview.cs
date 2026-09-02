using System.Collections.Generic;
using Godot;

namespace Xiangqi3D;

/// <summary>
/// GLB model preview tool: renders all .glb files in a directory from multiple
//  angles, measures AABB, and saves screenshots + a JSON summary.
///
/// Usage:
///   godot --script test/GlbPreview.cs -- --dir res://path/to/glb [--spin]
///   godot --script test/GlbPreview.cs -- --dir res://assets/glb
///
/// Without --dir, scans res://3DModel/glb/ (if it exists) or res://assets/glb/.
/// With --spin, slowly rotates the model for a video capture.
/// Output: screenshots/preview_<model>_<angle>.png + screenshots/glb_report.json
/// </summary>
public partial class GlbPreview : SceneTree
{
    private Node3D _world;
    private Camera3D _cam;
    private readonly List<(string Path, string Name)> _models = new();
    private int _idx;
    private int _angle;
    private float _t;
    private string _pendingSnap;
    private bool _spin;
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
        // parse --dir and --spin from command line
        string dir = null;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--dir=")) dir = arg.Substring(6);
            else if (arg == "--dir" || arg == "--spin") { if (arg == "--spin") _spin = true; }
        }
        // also check args without =
        var cmdArgs = OS.GetCmdlineArgs();
        for (int i = 0; i < cmdArgs.Length - 1; i++)
        {
            if (cmdArgs[i] == "--dir" && !cmdArgs[i + 1].StartsWith("--")) dir = cmdArgs[i + 1];
            if (cmdArgs[i] == "--spin") _spin = true;
        }

        if (string.IsNullOrEmpty(dir))
        {
            if (DirAccess.DirExistsAbsolute("res://3DModel/glb")) dir = "res://3DModel/glb";
            else if (DirAccess.DirExistsAbsolute("res://assets/glb")) dir = "res://assets/glb";
            else { GD.PrintErr("No GLB directory found. Use --dir=res://path/to/glb"); Quit(1); return; }
        }

        // scan for .glb files
        var da = DirAccess.Open(dir);
        if (da == null) { GD.PrintErr($"Cannot open: {dir}"); Quit(1); return; }
        da.ListDirBegin();
        string f;
        while ((f = da.GetNext()) != "")
        {
            if (f.EndsWith(".glb")) _models.Add(($"{dir}/{f}", f.GetBaseName()));
        }
        da.ListDirEnd();
        _models.Sort((a, b) => a.Name.CompareTo(b.Name));

        if (_models.Count == 0) { GD.PrintErr($"No .glb files in {dir}"); Quit(1); return; }

        // build world
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

        var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -35f, 0f), LightEnergy = 1.4f, ShadowEnabled = true };
        _world.AddChild(sun);
        var fill = new DirectionalLight3D { RotationDegrees = new Vector3(-30f, 140f, 0f), LightEnergy = 0.3f, LightColor = new Color(0.7f, 0.8f, 1.0f) };
        _world.AddChild(fill);

        // ground
        _world.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(60f, 0.2f, 60f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.35f, 0.30f), Roughness = 1f } },
            Position = new Vector3(0f, -0.1f, 0f),
        });

        // axis markers
        Marker(new Color(1f, 0.9f, 0.2f), new Vector3(0, 1f, 12f));   // +Z yellow
        Marker(new Color(1f, 0.3f, 0.9f), new Vector3(0, 1f, -12f));  // -Z magenta
        Marker(new Color(0.3f, 0.9f, 1f), new Vector3(12f, 1f, 0f));   // +X cyan
        Marker(new Color(1f, 1f, 1f), new Vector3(-12f, 1f, 0f));     // -X white

        _cam = new Camera3D { Fov = 45f, Near = 0.1f, Far = 300f };
        _world.AddChild(_cam);
        _cam.MakeCurrent();

        GD.Print($"GlbPreview: {_models.Count} models, spin={_spin}");
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;

        // save pending screenshot from previous frame
        if (_pendingSnap != null)
        {
            var img = Root.GetTexture().GetImage();
            img.SavePng($"res://screenshots/{_pendingSnap}");
            GD.Print($"  saved {_pendingSnap}");
            _pendingSnap = null;
        }

        // cleanup previous model (keep env/cam/ground/markers)
        if (_spin)
        {
            // slow rotation mode for video capture
            foreach (var child in _world.GetChildren())
                if (child is Node3D n && n.Name == "Model") n.RotateY((float)delta * 0.5f);
            return false;
        }

        // clear previous model
        for (int i = _world.GetChildCount() - 1; i >= 0; i--)
        {
            var child = _world.GetChild(i);
            if (child is Node3D n && n.Name == "Model") { _world.RemoveChild(child); child.QueueFree(); }
        }

        if (_idx >= _models.Count)
        {
            // save report
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

        var (path, name) = _models[_idx];
        var packed = GD.Load<PackedScene>(path);
        if (packed == null) { GD.PrintErr($"  missing: {path}"); _idx++; _angle = 0; return false; }

        var inst = packed.Instantiate<Node3D>();
        inst.Name = "Model";
        _world.AddChild(inst);

        // measure AABB
        var aabb = new Aabb();
        bool any = false;
        MeasureAabb(inst, Transform3D.Identity, ref aabb, ref any);
        var size = any ? aabb.Size : Vector3.Zero;

        if (_angle == 0)
        {
            GD.Print($"  {name}: size=({size.X:F2}, {size.Y:F2}, {size.Z:F2})");
            _report.Add(new Dictionary<string, Variant>
            {
                ["name"] = name, ["path"] = path,
                ["size_x"] = size.X, ["size_y"] = size.Y, ["size_z"] = size.Z,
            });
        }

        // position camera for current angle
        var (angleName, camPos, camTarget) = Angles[_angle];
        _cam.Position = camPos;
        _cam.LookAt(camTarget, Vector3.Up);
        _pendingSnap = $"preview_{name}_{angleName}.png";

        _angle++;
        if (_angle >= Angles.Length) { _angle = 0; _idx++; }
        return false;
    }

    private void MeasureAabb(Node3D node, Transform3D xform, ref Aabb acc, ref bool any)
    {
        var t = xform * node.Transform;
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            var local = mi.Mesh.GetAabb();
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(
                    (i & 1) == 0 ? local.Position.X : local.End.X,
                    (i & 2) == 0 ? local.Position.Y : local.End.Y,
                    (i & 4) == 0 ? local.Position.Z : local.End.Z);
                var w = t * c;
                acc = any ? acc.Expand(w) : new Aabb(w, Vector3.Zero);
                any = true;
            }
        }
        foreach (var child in node.GetChildren())
            if (child is Node3D n) MeasureAabb(n, t, ref acc, ref any);
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
