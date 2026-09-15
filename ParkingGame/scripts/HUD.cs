using System;
using Godot;

namespace ParkingGame;

/// <summary>CanvasLayer UI: prompt, gear letter, speed, timer, the「报告我停好了」
/// button, fail toast, start/verdict overlays. Every element is procedural
/// (SystemFont), no assets.</summary>
public partial class HUD : CanvasLayer
{
    private Label _prompt, _gear, _gearCap, _speed, _timer, _toast, _coins;
    private Button _report;
    private Control _startOverlay, _endOverlay;
    private Label _endTitle, _endStats;
    private float _toastTtl;

    /// <summary>Raised when the player asks for the parking verdict.</summary>
    public event Action ReportRequested;

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

        // top-left coins (garage economy)
        _coins = MkLabel("金币 0", 20, new Color(0.98f, 0.88f, 0.5f), Godot.HorizontalAlignment.Left);
        _coins.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        _coins.OffsetLeft = 18f; _coins.OffsetTop = 14f;
        _coins.OffsetRight = 200f; _coins.OffsetBottom = 44f;
        AddChild(_coins);

        BuildReportButton();
        BuildToast();
        BuildStartOverlay();
        BuildEndOverlay();
    }

    private void BuildReportButton()
    {
        _report = new Button
        {
            Text = "报告我停好了 (G)",
            // keyboard focus would let Space (handbrake) "click" the button
            FocusMode = Control.FocusModeEnum.None,
        };
        _report.AddThemeFontOverride("font", UiFont());
        _report.AddThemeFontSizeOverride("font_size", 24);
        _report.AddThemeColorOverride("font_color", new Color(0.97f, 0.98f, 0.94f));
        _report.AddThemeColorOverride("font_hover_color", Colors.White);
        _report.AddThemeColorOverride("font_pressed_color", new Color(0.85f, 1f, 0.85f));
        _report.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.65f, 0.6f));
        foreach (var (state, bg) in new[] {
                 ("normal", new Color(0.13f, 0.42f, 0.20f, 0.92f)),
                 ("hover", new Color(0.18f, 0.55f, 0.26f, 0.95f)),
                 ("pressed", new Color(0.10f, 0.34f, 0.16f, 0.95f)),
                 ("disabled", new Color(0.25f, 0.28f, 0.26f, 0.85f)) })
        {
            _report.AddThemeStyleboxOverride(state, new StyleBoxFlat
            {
                BgColor = bg,
                CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
                CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
                ContentMarginLeft = 22, ContentMarginRight = 22,
                ContentMarginTop = 10, ContentMarginBottom = 10,
            });
        }
        _report.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _report.OffsetLeft = -140f; _report.OffsetRight = 140f;
        _report.OffsetTop = -70f; _report.OffsetBottom = -18f;
        _report.Pressed += () => ReportRequested?.Invoke();
        AddChild(_report);
    }

    private void BuildToast()
    {
        _toast = MkLabel("", 22, new Color(1f, 0.62f, 0.55f), Godot.HorizontalAlignment.Center);
        _toast.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.10f, 0.10f, 0.85f),
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 18, ContentMarginRight = 18,
            ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _toast.OffsetLeft = -430f; _toast.OffsetRight = 430f;
        _toast.OffsetTop = 64f; _toast.OffsetBottom = 116f;
        _toast.Visible = false;
        AddChild(_toast);
    }

    public override void _Process(double delta)
    {
        if (_toastTtl > 0f)
        {
            _toastTtl -= (float)delta;
            if (_toastTtl <= 0f) _toast.Visible = false;
        }
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

        float y = 215f;
        foreach (var def in LevelDef.All)
        {
            var row = MkLabel(def.Title, 22, new Color(0.93f, 0.90f, 0.84f), Godot.HorizontalAlignment.Left);
            row.OffsetLeft = 400f; row.OffsetTop = y; row.OffsetRight = 950f; row.OffsetBottom = y + 34f;
            _startOverlay.AddChild(row);
            y += 38f;
        }

        var keys = MkLabel("↑ 油门 · ↓ 刹车 · ←→ 方向 · R/N/D 挂挡 · 空格 手刹 · G 报告我停好了 · 回车 重开",
            20, new Color(0.78f, 0.72f, 0.62f), Godot.HorizontalAlignment.Center);
        keys.OffsetLeft = 140f; keys.OffsetTop = 568f; keys.OffsetRight = 1140f; keys.OffsetBottom = 602f;
        _startOverlay.AddChild(keys);
        var pick = MkLabel("按 1-9 选关 · 0 自定义 · E 编辑器 · B 车库 · 回车从第 1 关开始", 22,
            new Color(1f, 0.88f, 0.6f), Godot.HorizontalAlignment.Center);
        pick.OffsetLeft = 240f; pick.OffsetTop = 612f; pick.OffsetRight = 1040f; pick.OffsetBottom = 648f;
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

    /// <summary>Verdict overlay content — grade, the geometry the check saw,
    /// and the coin payout (0 on scripted demo runs).</summary>
    public void SetVerdict(Game.ParkResult r, float seconds, int collisions, bool perfect, int reward)
    {
        _endTitle.Text = perfect ? "★ 完美入库！" : "√ 停车入位成功！";
        _endStats.Text =
            $"用时 {seconds:0.0} 秒　·　碰撞 {collisions} 次　·　角度误差 {r.AngleDeg:0.0}°" +
            $"　·　横向居中偏差 {Mathf.Abs(r.LatOff) * 100f:0} cm" +
            (reward > 0 ? $"　·　金币 +{reward}" : "");
    }

    public void SetCoins(int n) => _coins.Text = $"金币 {n}";

    public void SetReportEnabled(bool on) => _report.Disabled = !on;

    public void ShowFailToast(string[] reasons)
    {
        _toast.Text = "× 未通过：" + string.Join("；", reasons);
        _toast.Visible = true;
        _toastTtl = 4f;
    }
}
