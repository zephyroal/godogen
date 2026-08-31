using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>CanvasLayer UI: fortress score bars, HP, skill cooldowns, announcements, end screen.
/// Every element is anchor/container laid out so it adapts to any window size.</summary>
public partial class HUD : CanvasLayer
{
    private Label _announce;
    private Label _help;
    private Label _respawn;
    private ProgressBar _hpBar;
    private Label _hpText;
    private ProgressBar _blastCd;
    private ProgressBar _dashCd;
    private ProgressBar _blueScore;
    private ProgressBar _redScore;
    private Label _blueScoreText;
    private Label _redScoreText;
    private Control _endScreen;
    private Label _endTitle;
    private Label _endList;
    private float _announceT, _helpT;

    private static StyleBoxFlat SqStyle(Color fill, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        };
    }

    /// <summary>Anchor a control to a preset point with pixel offsets — correct at any window size.</summary>
    private static void Anchor(Control c, Control.LayoutPreset preset, float l, float t, float r, float b)
    {
        c.SetAnchorsAndOffsetsPreset(preset);
        c.OffsetLeft = l;
        c.OffsetTop = t;
        c.OffsetRight = r;
        c.OffsetBottom = b;
    }

    private static Label MkLabel(string text, int size, Color color, HorizontalAlignment halign = HorizontalAlignment.Left)
    {
        var l = new Label
        {
            Text = text,
            Modulate = color,
            HorizontalAlignment = halign,
        };
        l.AddThemeFontOverride("font", FX.UiFont());
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
        // tug-of-war top bar: 蓝方X/10 — coin — 红方X/10 (anchored top center)
        var top = new HBoxContainer();
        Anchor(top, Control.LayoutPreset.CenterTop, -370f, 12f, 370f, 46f);
        top.AddThemeConstantOverride("separation", 10);
        AddChild(top);

        top.AddChild(ScoreSide(leftToRight: true, out _blueScore, out _blueScoreText));

        var coin = new Panel { CustomMinimumSize = new Vector2(32f, 32f) };
        coin.AddThemeStyleboxOverride("panel", SqStyle(new Color(1f, 0.82f, 0.2f), new Color(0.8f, 0.6f, 0.1f)));
        top.AddChild(coin);

        top.AddChild(ScoreSide(leftToRight: false, out _redScore, out _redScoreText));
        UpdateFortressSquares();

        // announcements (top center, below the score bar)
        _announce = Shadowed(MkLabel("", 34, new Color(1f, 0.95f, 0.75f), HorizontalAlignment.Center),
            new Color(0f, 0f, 0f, 0.7f));
        Anchor(_announce, Control.LayoutPreset.CenterTop, -500f, 58f, 500f, 112f);
        AddChild(_announce);

        // help strip (bottom center)
        _help = MkLabel("A/D 变道 · S 掉头防守 · 空格 爆破 · Shift 冲刺撞击", 22, new Color(1f, 1f, 1f, 0.85f), HorizontalAlignment.Center);
        Anchor(_help, Control.LayoutPreset.BottomWide, 0f, -48f, 0f, -14f);
        AddChild(_help);
        _helpT = 12f;

        // HP bar bottom-left
        _hpBar = new ProgressBar
        {
            MinValue = 0, MaxValue = Game.PlayerHp, Value = Game.PlayerHp,
            CustomMinimumSize = new Vector2(300f, 24f),
            ShowPercentage = false,
        };
        Anchor(_hpBar, Control.LayoutPreset.BottomLeft, 24f, -80f, 324f, -56f);
        _hpBar.AddThemeStyleboxOverride("background", SqStyle(new Color(0.1f, 0.1f, 0.12f), new Color(0f, 0f, 0f)));
        _hpBar.AddThemeStyleboxOverride("fill", SqStyle(Game.Blue, new Color(0f, 0f, 0f)));
        AddChild(_hpBar);
        _hpText = MkLabel("生命 120", 20, new Color(1f, 1f, 1f));
        Anchor(_hpText, Control.LayoutPreset.BottomLeft, 28f, -106f, 240f, -82f);
        AddChild(_hpText);

        // skill cooldowns bottom-right: two columns, label above bar
        var skills = new HBoxContainer();
        Anchor(skills, Control.LayoutPreset.BottomRight, -258f, -112f, -24f, -54f);
        skills.AddThemeConstantOverride("separation", 8);
        AddChild(skills);
        _blastCd = SkillColumn(skills, "爆破 [空格]", new Color(1f, 0.6f, 0.2f));
        _dashCd = SkillColumn(skills, "冲刺 [Shift]", new Color(0.5f, 0.8f, 1f));

        // respawn overlay (screen center)
        _respawn = Shadowed(MkLabel("", 40, new Color(1f, 0.4f, 0.4f), HorizontalAlignment.Center),
            new Color(0f, 0f, 0f, 0.7f));
        Anchor(_respawn, Control.LayoutPreset.Center, -400f, -30f, 400f, 30f);
        AddChild(_respawn);

        BuildEndScreen();
    }

    /// <summary>One score side: progress bar with the caption drawn on top of it.</summary>
    private Control ScoreSide(bool leftToRight, out ProgressBar bar, out Label text)
    {
        var side = new Control { CustomMinimumSize = new Vector2(330f, 28f) };
        bar = MkScoreBar(leftToRight);
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        side.AddChild(bar);
        text = MkLabel("", 18, new Color(1f, 1f, 1f), HorizontalAlignment.Center);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        side.AddChild(text);
        return side;
    }

    private ProgressBar MkScoreBar(bool leftToRight)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = Game.FortressCount,
            Value = 0,
            CustomMinimumSize = new Vector2(330f, 28f),
            ShowPercentage = false,
            FillMode = (int)(leftToRight ? ProgressBar.FillModeEnum.BeginToEnd : ProgressBar.FillModeEnum.EndToBegin),
        };
        bar.AddThemeStyleboxOverride("background", SqStyle(new Color(0.1f, 0.1f, 0.12f), new Color(0.3f, 0.3f, 0.35f)));
        bar.AddThemeStyleboxOverride("fill", SqStyle(leftToRight ? Game.Blue : Game.Red, new Color(0f, 0f, 0f)));
        return bar;
    }

    private ProgressBar SkillColumn(HBoxContainer parent, string title, Color color)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        parent.AddChild(col);
        col.AddChild(MkLabel(title, 20, new Color(1f, 1f, 1f)));
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = 100,
            CustomMinimumSize = new Vector2(120f, 14f),
            ShowPercentage = false,
        };
        bar.AddThemeStyleboxOverride("background", SqStyle(new Color(0.1f, 0.1f, 0.12f), new Color(0f, 0f, 0f)));
        bar.AddThemeStyleboxOverride("fill", SqStyle(color, new Color(0f, 0f, 0f)));
        col.AddChild(bar);
        return bar;
    }

    private void BuildEndScreen()
    {
        _endScreen = new Control
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _endScreen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _endScreen.AddChild(dim);

        var box = new VBoxContainer();
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddThemeConstantOverride("separation", 18);
        _endScreen.AddChild(box);

        _endTitle = Shadowed(MkLabel("胜利！", 64, new Color(1f, 0.9f, 0.3f), HorizontalAlignment.Center),
            new Color(0f, 0f, 0f, 0.8f));
        _endTitle.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(_endTitle);
        _endList = MkLabel("", 22, new Color(0.9f, 0.9f, 0.9f), HorizontalAlignment.Center);
        _endList.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(_endList);
        var hint = MkLabel("按 R 重新开始", 26, new Color(1f, 1f, 1f), HorizontalAlignment.Center);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(hint);
        AddChild(_endScreen);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_announceT > 0f)
        {
            _announceT -= dt;
            float a = Mathf.Min(1f, _announceT / 0.5f);
            _announce.Modulate = new Color(1f, 0.95f, 0.75f, a);
            if (_announceT <= 0f) _announce.Text = "";
        }
        if (_helpT > 0f)
        {
            _helpT -= dt;
            _help.Modulate = new Color(1f, 1f, 1f, Mathf.Min(1f, _helpT));
            if (_helpT <= 0f) _help.Visible = false;
        }

        var p = Game.Instance?.Player;
        if (p != null && IsInstanceValid(p))
        {
            _hpBar.MaxValue = p.MaxHp;
            _hpBar.Value = Mathf.Max(0f, p.Hp);
            _hpText.Text = $"生命 {Mathf.CeilToInt(Mathf.Max(0f, p.Hp))}";
            _blastCd.Value = 100f * (1f - p.BlastCdTimer / Game.BlastCd);
            _dashCd.Value = 100f * (1f - p.DashCdTimer / Game.DashCd);
            _respawn.Text = p.Dead ? $"复活中 {p.RespawnTimer:0.0}s" : "";
        }
    }

    public void Announce(string text, float duration)
    {
        _announce.Text = text;
        _announceT = duration;
        _announce.Modulate = new Color(1f, 0.95f, 0.75f, 1f);
    }

    public void UpdateFortressSquares()
    {
        var game = Game.Instance;
        if (game == null) return;
        int blueConquest = 0, redConquest = 0;
        foreach (var f in game.Fortresses)
        {
            if (!f.Destroyed) continue;
            if (f.Team == Team.Red) blueConquest++; // blue destroyed a red fortress
            else redConquest++;
        }
        _blueScore.Value = blueConquest;
        _redScore.Value = redConquest;
        _blueScoreText.Text = $"蓝方 {blueConquest}/10";
        _redScoreText.Text = $"红方 {redConquest}/10";
    }

    public void ShowEndScreen(bool won, List<string> destroyedFortresses)
    {
        _endTitle.Text = won ? "胜利！" : "失败…";
        _endTitle.Modulate = won ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 0.45f, 0.4f);
        _endList.Text = destroyedFortresses.Count == 0
            ? "我方城池无一失守"
            : "被摧毁的城池：\n" + string.Join("  ", destroyedFortresses);
        _endScreen.Visible = true;
    }
}
