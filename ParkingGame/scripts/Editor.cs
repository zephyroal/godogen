using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace ParkingGame;

/// <summary>Mouse-driven level editor. Tools place walls / parked cars / cones /
/// pedestrians / the slot / the spawn point on the ground plane; W cycles the
/// weather, S saves levels/custom.json, T test-plays the current draft.
/// The saved level is playable from the start screen with key 0.</summary>
public partial class Editor : Node3D
{
    public enum Tool { Wall, Parked, Cone, Ped, Slot, Spawn, Delete }

    // ---- custom-level save format (plain floats — Godot structs aren't JSON-friendly) ----
    public sealed class WallDto { public float[] C { get; set; } = { 0, 0.5f, 0 }; public float[] S { get; set; } = { 4, 1, 0.4f }; }
    public sealed class ParkedDto { public float X { get; set; } public float Z { get; set; } public float Yaw { get; set; } public int Color { get; set; } }
    public sealed class PedDto { public float Ax { get; set; } public float Az { get; set; } public float Bx { get; set; } public float Bz { get; set; } }

    public sealed class CustomDto
    {
        public float[] Spawn { get; set; } = { 13f, 0.8f, 2.2f };
        public float SpawnYaw { get; set; } = -90f;
        public float[] SlotC { get; set; } = { 6.5f, 0, -2.6f };
        public float SlotYaw { get; set; } = 0f;
        public float SlotLen { get; set; } = 6f;
        public float SlotWid { get; set; } = 2.5f;
        public string Weather { get; set; } = "Sunny";
        public List<WallDto> Walls { get; set; } = new();
        public List<ParkedDto> Parked { get; set; } = new();
        public List<float[]> Cones { get; set; } = new();
        public List<PedDto> Peds { get; set; } = new();
    }

    private static readonly Color[] ParkColors =
    {
        new(0.75f, 0.77f, 0.80f), new(0.25f, 0.40f, 0.75f),
        new(0.30f, 0.60f, 0.40f), new(0.90f, 0.75f, 0.20f),
    };

    public static string CustomPath => ProjectSettings.GlobalizePath("res://levels/custom.json");

    public static void SaveCustom(CustomDto dto)
    {
        var dir = Path.GetDirectoryName(CustomPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(CustomPath, JsonSerializer.Serialize(dto));
    }

    public static CustomDto LoadCustomDto()
    {
        try
        {
            if (File.Exists(CustomPath))
                return JsonSerializer.Deserialize<CustomDto>(File.ReadAllText(CustomPath));
        }
        catch { /* unreadable — ignore */ }
        return null!;
    }

    public static LevelDef DtoToDef(CustomDto d)
    {
        var def = new LevelDef
        {
            Title = "自定义关卡",
            Hint = "编辑器制作——停好后按 G 报告判定",
            Spawn = new Vector3(d.Spawn[0], d.Spawn[1], d.Spawn[2]),
            SpawnYawDeg = d.SpawnYaw,
            SlotCenter = new Vector3(d.SlotC[0], d.SlotC[1], d.SlotC[2]),
            SlotYawDeg = d.SlotYaw,
            SlotLen = d.SlotLen,
            SlotWid = d.SlotWid,
            Weather = System.Enum.TryParse<WeatherKind>(d.Weather, out var w) ? w : WeatherKind.Sunny,
            CamH = 18f, CamBack = 9f,
        };
        def.Walls.AddRange(d.Walls.ConvertAll(wl =>
            new BoxDef(new Vector3(wl.C[0], wl.C[1], wl.C[2]), new Vector3(wl.S[0], wl.S[1], wl.S[2]))));
        def.Parked.AddRange(d.Parked.ConvertAll(p =>
            new ParkedDef(new Vector3(p.X, 0, p.Z), p.Yaw,
                ParkColors[System.Math.Clamp(p.Color, 0, ParkColors.Length - 1)])));
        def.Cones.AddRange(d.Cones.ConvertAll(c => new Vector3(c[0], 0, c[1])));
        def.Hazards.AddRange(d.Peds.ConvertAll(p => new HazardDef(HazardKind.Pedestrian,
            new[] { new Vector3(p.Ax, 0, p.Az), new Vector3(p.Bx, 0, p.Bz) }, 1.1f)));
        return def;
    }

    // ---- editor instance ----
    private Game _game = null!;
    private Tool _tool = Tool.Wall;
    private bool _wallHorizontal = true;
    private float _parkedYaw;
    private int _parkedColor;
    private Vector3 _pedA;
    private bool _pedFirstClick;
    private WeatherKind _weather = WeatherKind.Sunny;

    // draft state
    private Vector3 _spawn = new(13f, 0.8f, 2.2f);
    private float _spawnYaw = -90f;
    private Vector3 _slotC = new(6.5f, 0, -2.6f);
    private float _slotYaw;
    private float _slotLen = 6f, _slotWid = 2.5f;
    private readonly List<WallDto> _walls = new();
    private readonly List<ParkedDto> _parked = new();
    private readonly List<float[]> _cones = new();
    private readonly List<PedDto> _peds = new();

    private CanvasLayer _ui = null!;
    private Label _status = null!;
    private MeshInstance3D _ghost = null!;
    private Vector3 _mouse = new(7, 0, 0);
    private bool _active = true;

    /// <summary>True while the palette is up and the editor owns the mouse/camera.</summary>
    public bool Active => _active;

    public void Setup(Game game)
    {
        _game = game;

        var existing = LoadCustomDto();
        if (existing != null)
            Import(existing);
        else
            SeedTemplate();

        _ghost = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(4, 0.15f, 0.4f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.3f, 1f, 0.4f, 0.4f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        AddChild(_ghost);

        BuildPalette();
        Rebuild();
    }

    private void SeedTemplate()
    {
        _walls.Add(new WallDto { C = new[] { 7f, 0.175f, -4.6f }, S = new[] { 24f, 0.35f, 0.35f } });
        _walls.Add(new WallDto { C = new[] { 7f, 0.5f, 6.6f }, S = new[] { 24f, 1f, 0.4f } });
        _walls.Add(new WallDto { C = new[] { -4.5f, 0.5f, 1f }, S = new[] { 0.4f, 1f, 11f } });
        _walls.Add(new WallDto { C = new[] { 18.5f, 0.5f, 1f }, S = new[] { 0.4f, 1f, 11f } });
    }

    private void Import(CustomDto d)
    {
        _spawn = new Vector3(d.Spawn[0], d.Spawn[1], d.Spawn[2]);
        _spawnYaw = d.SpawnYaw;
        _slotC = new Vector3(d.SlotC[0], d.SlotC[1], d.SlotC[2]);
        _slotYaw = d.SlotYaw;
        _slotLen = d.SlotLen;
        _slotWid = d.SlotWid;
        System.Enum.TryParse<WeatherKind>(d.Weather, out _weather);
        _walls.Clear(); _walls.AddRange(d.Walls);
        _parked.Clear(); _parked.AddRange(d.Parked);
        _cones.Clear(); _cones.AddRange(d.Cones);
        _peds.Clear(); _peds.AddRange(d.Peds);
    }

    private void BuildPalette()
    {
        _ui = new CanvasLayer();
        var box = new VBoxContainer();
        box.OffsetLeft = 16; box.OffsetTop = 90;
        box.AddThemeConstantOverride("separation", 4);
        _ui.AddChild(box);

        void AddBtn(string text, Tool tool)
        {
            var b = MkButton(text);
            b.Pressed += () => SelectTool(tool);
            box.AddChild(b);
        }
        AddBtn("1 墙块", Tool.Wall);
        AddBtn("2 停放车", Tool.Parked);
        AddBtn("3 锥桶", Tool.Cone);
        AddBtn("4 行人", Tool.Ped);
        AddBtn("5 库位", Tool.Slot);
        AddBtn("6 出生点", Tool.Spawn);
        AddBtn("7 删除", Tool.Delete);

        var wb = MkButton("W 天气循环");
        wb.Pressed += CycleWeather;
        box.AddChild(wb);
        var tb = MkButton("T 测试试玩");
        tb.Pressed += BeginTest;
        box.AddChild(tb);
        var sb = MkButton("S 保存关卡");
        sb.Pressed += Save;
        box.AddChild(sb);

        _status = MkLabel("编辑器 · 左键放置 · R 旋转 · [ ] 车位长 · - = 车位宽", 16, new Color(0.9f, 0.9f, 0.85f));
        _status.OffsetLeft = 420; _status.OffsetTop = 680;
        _ui.AddChild(_status);
        AddChild(_ui);
    }

    private static Button MkButton(string text)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontOverride("font", new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" },
        });
        b.AddThemeFontSizeOverride("font_size", 18);
        return b;
    }

    private static Label MkLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text, Modulate = color };
        l.AddThemeFontOverride("font", new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" },
        });
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    // ---- editing ----

    private void SelectTool(Tool t)
    {
        _tool = t;
        _pedFirstClick = false;
        SetStatus($"工具: {t}（左键放置 · R 旋转 · Esc 退出）");
    }

    private void SetStatus(string s) => _status.Text = $"[编辑器] {s}　·　天气: {_weather}";

    private void CycleWeather()
    {
        _weather = _weather switch
        {
            WeatherKind.Sunny => WeatherKind.Rain,
            WeatherKind.Rain => WeatherKind.Snow,
            WeatherKind.Snow => WeatherKind.Blaze,
            _ => WeatherKind.Sunny,
        };
        Rebuild();
    }

    private void Save()
    {
        SaveCustom(DtoFromState());
        _game.SetCustomDef(DtoToDef(DtoFromState()));
        SetStatus("已保存 → 开局界面按 0 试玩");
    }

    private void BeginTest()
    {
        _active = false;
        _ui.Visible = false;
        _ghost.Visible = false;
        _game.BeginEditorTest();
    }

    private void EndTest()
    {
        _active = true;
        _ui.Visible = true;
        Rebuild();
    }

    private void Rebuild() => _game.EditorPreview(DraftDef());

    private CustomDto DtoFromState() => new()
    {
        Spawn = new[] { _spawn.X, _spawn.Y, _spawn.Z },
        SpawnYaw = _spawnYaw,
        SlotC = new[] { _slotC.X, _slotC.Y, _slotC.Z },
        SlotYaw = _slotYaw,
        SlotLen = _slotLen,
        SlotWid = _slotWid,
        Weather = _weather.ToString(),
        Walls = new List<WallDto>(_walls),
        Parked = new List<ParkedDto>(_parked),
        Cones = new List<float[]>(_cones),
        Peds = new List<PedDto>(_peds),
    };

    private LevelDef DraftDef() => DtoToDef(DtoFromState());

    // ---- per-frame: editor camera + ghost follow ----

    public override void _Process(double delta)
    {
        if (!_active) return;
        var cam = GetViewport().GetCamera3D();
        cam.GlobalPosition = new Vector3(7f, 32f, 0.5f);
        cam.LookAt(new Vector3(7f, 0, 0.5f), Vector3.Up);

        _ghost.GlobalPosition = _mouse + Vector3.Up * 0.1f;
        float ghostYaw = 0f;
        if (_tool == Tool.Parked) ghostYaw = Mathf.DegToRad(_parkedYaw);
        else if (_tool == Tool.Wall && !_wallHorizontal) ghostYaw = Mathf.Pi / 2f;
        _ghost.Rotation = new Vector3(0, ghostYaw, 0);
        _ghost.Visible = _tool != Tool.Delete;
        if (_ghost.Mesh is BoxMesh bm)
        {
            bm.Size = _tool switch
            {
                Tool.Wall => _wallHorizontal ? new Vector3(4, 0.15f, 0.4f) : new Vector3(0.4f, 0.15f, 4),
                Tool.Parked => new Vector3(1.8f, 0.15f, 4.6f),
                Tool.Cone or Tool.Ped or Tool.Spawn or Tool.Slot or Tool.Delete => new Vector3(1, 0.15f, 1),
                _ => new Vector3(1, 0.15f, 1),
            };
        }
    }

    // ---- input ----

    public override void _Input(InputEvent ev)
    {
        if (!_active)
        {
            // test mode: Esc is the only way back to the editor
            if (ev is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Escape)
            {
                _game.EndEditorTest();
                EndTest();
            }
            return;
        }

        if (ev is InputEventMouseMotion mm)
        {
            _mouse = GroundPoint(mm.Position);
            return;
        }

        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            Place();
            return;
        }

        if (ev is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Key1: SelectTool(Tool.Wall); break;
                case Key.Key2: SelectTool(Tool.Parked); break;
                case Key.Key3: SelectTool(Tool.Cone); break;
                case Key.Key4: SelectTool(Tool.Ped); break;
                case Key.Key5: SelectTool(Tool.Slot); break;
                case Key.Key6: SelectTool(Tool.Spawn); break;
                case Key.Key7: SelectTool(Tool.Delete); break;
                case Key.R: RotateTool(); break;
                case Key.Bracketleft: _slotLen = Mathf.Clamp(_slotLen - 0.3f, 4.6f, 8f); Rebuild(); break;
                case Key.Bracketright: _slotLen = Mathf.Clamp(_slotLen + 0.3f, 4.6f, 8f); Rebuild(); break;
                case Key.Minus: _slotWid = Mathf.Clamp(_slotWid - 0.1f, 2.1f, 3.4f); Rebuild(); break;
                case Key.Equal: _slotWid = Mathf.Clamp(_slotWid + 0.1f, 2.1f, 3.4f); Rebuild(); break;
                case Key.W: CycleWeather(); break;
                case Key.S: Save(); break;
                case Key.T: BeginTest(); break;
                case Key.Escape: _game.CloseEditor(); break;
            }
        }
    }

    private void RotateTool()
    {
        switch (_tool)
        {
            case Tool.Wall: _wallHorizontal = !_wallHorizontal; break;
            case Tool.Parked: _parkedYaw += 45f; break;
            case Tool.Slot: _slotYaw += 45f; Rebuild(); break;
        }
    }

    private void Place()
    {
        Vector3 p = _mouse with { Y = 0 };
        switch (_tool)
        {
            case Tool.Wall:
                _walls.Add(new WallDto
                {
                    C = new[] { p.X, 0.5f, p.Z },
                    S = _wallHorizontal ? new[] { 4f, 1f, 0.4f } : new[] { 0.4f, 1f, 4f },
                });
                Rebuild();
                break;
            case Tool.Parked:
                _parked.Add(new ParkedDto { X = p.X, Z = p.Z, Yaw = _parkedYaw, Color = _parkedColor });
                _parkedColor = (_parkedColor + 1) % ParkColors.Length;
                Rebuild();
                break;
            case Tool.Cone:
                _cones.Add(new[] { p.X, p.Z });
                Rebuild();
                break;
            case Tool.Ped:
                if (!_pedFirstClick) { _pedA = p; _pedFirstClick = true; SetStatus("行人：再点一次确定终点"); }
                else
                {
                    _peds.Add(new PedDto { Ax = _pedA.X, Az = _pedA.Z, Bx = p.X, Bz = p.Z });
                    _pedFirstClick = false;
                    Rebuild();
                }
                break;
            case Tool.Slot:
                _slotC = p;
                Rebuild();
                break;
            case Tool.Spawn:
                _spawn = p with { Y = 0.8f };
                Rebuild();
                break;
            case Tool.Delete:
                DeleteNear(p);
                break;
        }
    }

    private void DeleteNear(Vector3 p)
    {
        float best = 2.5f;
        int idx = -1;
        int kind = 0; // 0 walls, 1 parked, 2 cones, 3 peds

        for (int i = 0; i < _walls.Count; i++)
        {
            float d = new Vector2(_walls[i].C[0] - p.X, _walls[i].C[2] - p.Z).Length();
            if (d < best) { best = d; idx = i; kind = 0; }
        }
        for (int i = 0; i < _parked.Count; i++)
        {
            float d = new Vector2(_parked[i].X - p.X, _parked[i].Z - p.Z).Length();
            if (d < best) { best = d; idx = i; kind = 1; }
        }
        for (int i = 0; i < _cones.Count; i++)
        {
            float d = new Vector2(_cones[i][0] - p.X, _cones[i][1] - p.Z).Length();
            if (d < best) { best = d; idx = i; kind = 2; }
        }
        for (int i = 0; i < _peds.Count; i++)
        {
            float dx = (_peds[i].Ax + _peds[i].Bx) / 2f - p.X;
            float dz = (_peds[i].Az + _peds[i].Bz) / 2f - p.Z;
            if (new Vector2(dx, dz).Length() < best) { best = new Vector2(dx, dz).Length(); idx = i; kind = 3; }
        }

        if (idx < 0) return;
        switch (kind)
        {
            case 0: _walls.RemoveAt(idx); break;
            case 1: _parked.RemoveAt(idx); break;
            case 2: _cones.RemoveAt(idx); break;
            case 3: _peds.RemoveAt(idx); break;
        }
        Rebuild();
    }

    private Vector3 GroundPoint(Vector2 screen)
    {
        var cam = GetViewport().GetCamera3D();
        Vector3 o = cam.ProjectRayOrigin(screen);
        Vector3 d = cam.ProjectRayNormal(screen);
        float t = -o.Y / d.Y;
        return o + d * t;
    }
}
