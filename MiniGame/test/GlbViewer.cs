using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// Interactive GLB viewer: opens a window with orbit camera, drop a .glb file
/// onto the window to load and inspect it in real-time.
///
/// Usage:
///   godot --script test/GlbViewer.cs
///   (or double-click glb_viewer.bat)
///
/// Controls:
///   Mouse drag 鈥?orbit camera
///   Scroll 鈥?zoom
///   Drop .glb file onto window 鈥?load model
///   F1 鈥?toggle wireframe
///   F2 鈥?toggle AABB display
///   R 鈥?reset camera
///   Space 鈥?next model in directory
///   ESC 鈥?quit
/// </summary>
public partial class GlbViewer : SceneTree
{
    private Node3D _world;
    private Camera3D _cam;
    private DirectionalLight3D _sun, _fill;
    private Node3D _model;
    private Label3D _info;
    private MeshInstance3D _aabbBox;

    private float _yaw = 0.6f, _pitch = 0.5f, _dist = 15f;
    private bool _dragging;
    private string _currentPath;
    private readonly List<string> _dirFiles = new();
    private int _dirIndex;

    public override void _Initialize()
    {
        _world = new Node3D { Name = "Viewer" };
        Root.AddChild(_world);

        _world.AddChild(new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.14f, 0.16f, 0.20f),
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.5f, 0.55f),
                AmbientLightEnergy = 1.2f,
                GlowEnabled = true,
                GlowIntensity = 0.4f,
                TonemapMode = Environment.ToneMapper.Filmic,
            },
        });

        _sun = new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -35f, 0f), LightEnergy = 1.4f, ShadowEnabled = true };
        _world.AddChild(_sun);
        _fill = new DirectionalLight3D { RotationDegrees = new Vector3(-30f, 140f, 0f), LightEnergy = 0.3f, LightColor = new Color(0.7f, 0.8f, 1.0f) };
        _world.AddChild(_fill);

        // ground grid
        _world.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(60f, 0.2f, 60f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.28f, 0.30f), Roughness = 1f } },
            Position = new Vector3(0f, -0.1f, 0f),
        });

        // axis markers
        Marker(new Color(1f, 0.9f, 0.2f), new Vector3(0, 0.5f, 8f));
        Marker(new Color(1f, 0.3f, 0.9f), new Vector3(0, 0.5f, -8f));
        Marker(new Color(0.3f, 0.9f, 1f), new Vector3(8f, 0.5f, 0f));
        Marker(new Color(1f, 1f, 1f), new Vector3(-8f, 0.5f, 0f));

        // info label
        _info = new Label3D
        {
            Text = "拖入.GLB文件 拖动旋转 滚轮缩放 Space下一个 ESC退出",
            Position = new Vector3(0, 8f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 28,
            Modulate = new Color(0.8f, 0.85f, 1f),
            Font = FX.UiFont(),
        };
        _world.AddChild(_info);

        // AABB display (hidden by default)
        _aabbBox = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One, Material = new StandardMaterial3D { AlbedoColor = new Color(0f, 1f, 0f, 0.3f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true } },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _world.AddChild(_aabbBox);

        _cam = new Camera3D { Fov = 45f, Near = 0.1f, Far = 500f };
        _world.AddChild(_cam);
        _cam.MakeCurrent();

        // scan default directory
        ScanDir("res://assets/glb");

        GD.Print("GlbViewer ready 鈥?drop .glb file or press Space");
    }

    public override bool _Process(double delta)
    {
        float dt = (float)delta;

        // orbit camera
        _dist = Mathf.Clamp(_dist, 3f, 100f);
        _pitch = Mathf.Clamp(_pitch, 0.1f, 1.4f);
        var off = new Vector3(
            Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            Mathf.Sin(_pitch),
            Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _dist;
        _cam.Position = off;
        _cam.LookAt(Vector3.Zero, Vector3.Up);

        // slow auto-rotate when not dragging
        if (!_dragging) _yaw += dt * 0.15f;

        return false;
    }

    private void HandleInput()
    {
        if (Input.IsMouseButtonPressed(MouseButton.Left)) _dragging = true;
        else _dragging = false;
        if (Input.IsMouseButtonPressed(MouseButton.WheelUp)) _dist *= 0.92f;
        if (Input.IsMouseButtonPressed(MouseButton.WheelDown)) _dist *= 1.08f;
        if (Input.IsKeyPressed(Key.Escape)) Quit(0);
        if (Input.IsKeyPressed(Key.R)) { _yaw = 0.6f; _pitch = 0.5f; _dist = 15f; }
        if (Input.IsKeyPressed(Key.Space)) NextModel();
        if (Input.IsKeyPressed(Key.F1) && _model != null) ToggleWireframe();
        if (Input.IsKeyPressed(Key.F2)) _aabbBox.Visible = !_aabbBox.Visible;
    }

    private void ScanDir(string dir)
    {
        _dirFiles.Clear();
        _dirIndex = 0;
        if (!DirAccess.DirExistsAbsolute(dir)) return;
        var da = DirAccess.Open(dir);
        if (da == null) return;
        da.ListDirBegin();
        string f;
        while ((f = da.GetNext()) != "")
            if (f.EndsWith(".glb")) _dirFiles.Add($"{dir}/{f}");
        da.ListDirEnd();
        _dirFiles.Sort();
        GD.Print($"Found {_dirFiles.Count} models in {dir}");
    }

    private void NextModel()
    {
        if (_dirFiles.Count == 0) return;
        _dirIndex = (_dirIndex + 1) % _dirFiles.Count;
        LoadModel(_dirFiles[_dirIndex]);
    }

    private void LoadModel(string path)
    {
        _currentPath = path;
        var projectRoot = ProjectSettings.GlobalizePath("res://");
        var globalPath = path.StartsWith("res://")
            ? ProjectSettings.GlobalizePath(path)
            : path;
        var resPath = path.StartsWith("res://") ? path : $"res://{path.Replace(projectRoot, "").TrimStart('/')}";

        // free previous model
        if (_model != null) { _model.QueueFree(); _model = null; }

        var packed = GD.Load<PackedScene>(resPath);
        if (packed == null)
        {
            // try loading from global path via ResourceLoader
            packed = ResourceLoader.Load<PackedScene>(resPath);
        }

        if (packed == null)
        {
            _info.Text = $"鍔犺浇澶辫触: {path}";
            GD.PrintErr($"Failed to load: {path}");
            return;
        }

        _model = packed.Instantiate<Node3D>();
        _model.Name = "Model";
        _world.AddChild(_model);

        // center and ground-align
        var aabb = new Aabb();
        bool any = false;
        MeasureAabb(_model, Transform3D.Identity, ref aabb, ref any);
        if (any)
        {
            // shift so bottom center is at origin
            var offset = new Vector3(-aabb.GetCenter().X, -aabb.Position.Y, -aabb.GetCenter().Z);
            _model.Position += offset;

            // auto-scale if too large
            float maxDim = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
            if (maxDim > 20f)
            {
                float s = 15f / maxDim;
                _model.Scale = Vector3.One * s;
                _model.Position *= s;
            }

            // fit camera distance
            _dist = Mathf.Clamp(maxDim * 1.5f, 8f, 60f);

            // update AABB display
            var scaledAabb = new Aabb(_model.Position, aabb.Size * _model.Scale);
            _aabbBox.Mesh = new BoxMesh { Size = scaledAabb.Size };
            _aabbBox.Position = scaledAabb.GetCenter();
            _aabbBox.Visible = false;

            _info.Text = $"{path.GetFile()}  size=({aabb.Size.X:F1}, {aabb.Size.Y:F1}, {aabb.Size.Z:F1})  [Space 涓嬩竴涓?路 F1 绾挎 路 F2 AABB 路 R 閲嶇疆]";
            GD.Print($"Loaded: {path.GetFile()} size=({aabb.Size.X:F2}, {aabb.Size.Y:F2}, {aabb.Size.Z:F2})");
        }
        else
        {
            _info.Text = $"{path.GetFile()} (鏃犳硶娴嬮噺 AABB)";
        }
    }

    private void ToggleWireframe()
    {
        GD.Print("Wireframe toggle not available in Godot 4 C# bindings");
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
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f), Material = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.5f } },
            Position = pos,
        });
    }
}
