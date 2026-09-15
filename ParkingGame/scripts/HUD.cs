using Godot;

namespace ParkingGame;

/// <summary>CanvasLayer UI: prompt, gear letter, speed, timer, start/end overlays.
/// Every element is procedural (SystemFont), no assets.</summary>
public partial class HUD : CanvasLayer
{
    private Label _prompt, _gear, _gearCap, _speed, _timer;
    private Control _startOverlay, _endOverlay;
    private Label _endTitle, _endStats;

    private static SystemFont UiFont() => new()
    {
        FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" },
    };

    private static Label MkLabel(string text, int size, Color color, Godot.HorizontalAlignment halign)
    {
        var l = new Label { Text = text, Modulate = color, HorizontalAlignment = halign };
        l.AddThemeFontOverride("font", UiFont());
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    private static Label Shadowed(Label l, Color shadow)
    {
        l.AddThemeColorOverride("font_shadow_color", shadow);
        l.AddThemeConstantOverride("shadow_offset_x", 2);
        l.AddThemeConstantOverride("shadow_offset_y", 2);
        return l;
    }

    public override void _Ready()
    {
        // top-center prompt
        _prompt = Shadowed(MkLabel("", 24, new Color(1f, 0.96f, 0.88f), Godot.HorizontalAlignment.Center),
            new Color(0f, 0f, 0f, 0.8f));
        _prompt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _prompt.OffsetLeft = -420f; _prompt.OffsetRight = 420f;
        _prompt.OffsetTop = 12f; _prompt.OffsetBottom = 52f;
        AddChild(_prompt);

        // bottom-left gear block
        _gear = Shadowed(MkLabel("N", 64, new Color(0.92f, 0.90f, 0.86f), Godot.HorizontalAlignment.Center),
            new Color(0f, 0f, 0f, 0.85f));
        _gear.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _gear.OffsetLeft = 24f; _gear.OffsetRight = 104f;
        _gear.OffsetTop = -96f; _gear.OffsetBottom = -24f;
        AddChild(_gear);
        _gearCap = MkLabel("档位 R/N/D", 14, new Color(0.8f, 0.78f, 0.72f), Godot.HorizontalAlignment.Left);
        _gearCap.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _gearCap.OffsetLeft = 26f; _gearCap.OffsetTop = -118f; _gearCap.OffsetRight = 240f; _gearCap.OffsetBottom = -98f;
        AddChild(_gearCap);

        // bottom-right speed + steering
        _speed = Shadowed(MkLabel("0 km/h", 34, new Color(0.92f, 0.90f, 0.86f), Godot.HorizontalAlignment.Right),
            new Color(0f, 0f, 0f, 0.85f));
        _speed.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        _speed.OffsetLeft = -220f; _speed.OffsetRight = -24f;
        _speed.OffsetTop = -84f; _speed.OffsetBottom = -36f;
        AddChild(_speed);

        // top-right timer / collisions
        _timer = MkLabel("0.0s · 碰撞 0", 20, new Color(0.9f, 0.92f, 0.95f), Godot.HorizontalAlignment.Right);
        _timer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _timer.OffsetLeft = -240f; _timer.OffsetRight = -18f;
        _timer.OffsetTop = 14f; _timer.OffsetBottom = 44f;
        AddChild(_timer);

        BuildStartOverlay();
        BuildEndOverlay();
    }

    private void BuildStartOverlay()
    {
        _startOverlay = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _startOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _startOverlay.AddChild(dim);

        var title = Shadowed(MkLabel("3D 倒车入库", 64, new Color(1f, 0.88f, 0.6f), Godot.HorizontalAlignment.Center),
            new Color(0f, 0f, 0f, 0.8f));
        title.OffsetLeft = 240f; title.OffsetTop = 90f;
        title.OffsetRight = 1040f; title.OffsetBottom = 170f;
        _startOverlay.AddChild(title);

        var sub = MkLabel("挂挡 · 油门 · 刹车 · 方向盘 —— 把车倒进绿色车位",
            24, new Color(0.93f, 0.88f, 0.80f), Godot.HorizontalAlignment.Center);
        sub.OffsetLeft = 240f; sub.OffsetTop = 175f; sub.OffsetRight = 1040f; sub.OffsetBottom = 210f;
        _startOverlay.AddChild(sub);

        float y = 250f;
        foreach (var def in LevelDef.All)
        {
            var row = MkLabel(def.Title, 24, new Color(0.93f, 0.90f, 0.84f), Godot.HorizontalAlignment.Left);
            row.OffsetLeft = 400f; row.OffsetTop = y; row.OffsetRight = 950f; row.OffsetBottom = y + 36f;
            _startOverlay.AddChild(row);
            y += 44f;
        }

        var keys = MkLabel("↑ 油门 · ↓ 刹车 · ←→ 方向 · R/N/D 挂挡 · 空格 手刹 · 回车 重开",
            20, new Color(0.78f, 0.72f, 0.62f), Godot.HorizontalAlignment.Center);
        keys.OffsetLeft = 140f; keys.OffsetTop = 560f; keys.OffsetRight = 1140f; keys.OffsetBottom = 596f;
        _startOverlay.AddChild(keys);
        var pick = MkLabel("按 1-6 选关 · 回车从第 1 关开始", 22, new Color(1f, 0.88f, 0.6f),
            Godot.HorizontalAlignment.Center);
        pick.OffsetLeft = 340f; pick.OffsetTop = 610f; pick.OffsetRight = 940f; pick.OffsetBottom = 646f;
        _startOverlay.AddChild(pick);
        AddChild(_startOverlay);
    }

    private void BuildEndOverlay()
    {
        _endOverlay = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _endOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.62f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _endOverlay.AddChild(dim);

        var box = new VBoxContainer();
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddThemeConstantOverride("separation", 22);
        _endOverlay.AddChild(box);

        _endTitle = Shadowed(MkLabel("√ 停车入位成功！", 62, new Color(0.55f, 1f, 0.65f),
            Godot.HorizontalAlignment.Center), new Color(0f, 0.25f, 0f, 0.85f));
        _endTitle.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(_endTitle);

        _endStats = MkLabel("", 28, new Color(0.95f, 0.92f, 0.86f), Godot.HorizontalAlignment.Center);
        _endStats.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(_endStats);

        var hint = MkLabel("回车 · 下一关      退格 · 重试本关", 24, new Color(1f, 0.88f, 0.6f),
            Godot.HorizontalAlignment.Center);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(hint);
        AddChild(_endOverlay);
    }

    public void ShowStart(bool on) => _startOverlay.Visible = on;
    public bool StartVisible => _startOverlay.Visible;
    public void ShowEnd(bool on) => _endOverlay.Visible = on;

    public void SetPrompt(string title, string hint)
    {
        _prompt.Text = $"{title}　——　{hint}";
    }

    public void SetGear(Car.Gear g)
    {
        _gear.Text = g.ToString();
        _gear.Modulate = g switch
        {
            Car.Gear.R => new Color(1f, 0.35f, 0.28f),
            Car.Gear.D => new Color(0.45f, 0.95f, 0.55f),
            _ => new Color(0.92f, 0.90f, 0.86f),
        };
    }

    public void SetSpeed(float kmh, float steerDeg) =>
        _speed.Text = $"{kmh:0} km/h   方向 {steerDeg:+0;-0}°";

    public void SetTimer(float seconds, int collisions) =>
        _timer.Text = $"{seconds:0.0}s · 碰撞 {collisions}";

    public void SetEndStats(float seconds, int collisions)
    {
        _endTitle.Text = "√ 停车入位成功！";
        _endStats.Text = $"用时 {seconds:0.0} 秒　·　碰撞 {collisions} 次";
    }
}
