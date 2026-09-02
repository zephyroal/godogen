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
    private float _prevBlastCd, _prevDashCd;
    private float _prevHp;
    private Control _startOverlay;
    public bool GameStarted { get; private set; }
    public void SetStart()
    {
        GameStarted = true;
        if (_startOverlay != null) _startOverlay.Visible = false;
    }

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

        // HP bar bottom-left
        _hpBar = new ProgressBar
        {
            MinValue = 0, MaxValue = Game.PlayerHp, Value = Game.PlayerHp,
            CustomMinimumSize = new Vector2(300f, 24f),
            ShowPercentage = false,
        };
        Anchor(_hpBar, Control.LayoutPreset.BottomLeft, 24f, -80f, 324f, -56f);
        _hpBar.AddThemeStyleboxOverride("background", SqStyle(new Color(0.1f, 0.1f, 0.12f), new Color(0f, 0f, 0f)));
        var fillStyle = SqStyle(Game.Blue, new Color(0f, 0f, 0f));
        _hpBar.AddThemeStyleboxOverride("fill", fillStyle);
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
        BuildStartOverlay();
    }

    private void BuildStartOverlay()
    {
        _startOverlay = new Control();
        _startOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _startOverlay.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_startOverlay);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _startOverlay.AddChild(dim);

        // Title
        var title = Shadowed(MkLabel("堡垒冲刺", 54, new Color(1f, 0.88f, 0.4f), HorizontalAlignment.Center), new Color(0f, 0f, 0f, 0.8f));
        title.OffsetLeft = 340f; title.OffsetTop = 120f;
        title.OffsetRight = 940f; title.OffsetBottom = 200f;
        _startOverlay.AddChild(title);

        // Two buttons
        float btnW = 280f, btnH = 68f, gap = 14f;
        float btnX = (1280 - btnW) / 2f;
        float y0 = 260f;

        var startBtn = new Button { Text = "开始游戏", CustomMinimumSize = new Vector2(btnW, btnH) };
        startBtn.AddThemeFontOverride("font", FX.UiFont());
        startBtn.AddThemeFontSizeOverride("font_size", 24);
        startBtn.OffsetLeft = btnX; startBtn.OffsetTop = y0;
        startBtn.OffsetRight = btnX + btnW; startBtn.OffsetBottom = y0 + btnH;
        startBtn.Pressed += () => { GameStarted = true; UIAnimator.FadeOut(_startOverlay, 0.2f, true); Game.Instance?.Hud?.Announce("摧毁红方 10 号终极主城即可获胜！", 4f); };
        _startOverlay.AddChild(startBtn);

        float y1 = y0 + btnH + gap;
        var spectateBtn = new Button { Text = "联机观战", CustomMinimumSize = new Vector2(btnW, btnH) };
        spectateBtn.AddThemeFontOverride("font", FX.UiFont());
        spectateBtn.AddThemeFontSizeOverride("font_size", 24);
        spectateBtn.OffsetLeft = btnX; spectateBtn.OffsetTop = y1;
        spectateBtn.OffsetRight = btnX + btnW; spectateBtn.OffsetBottom = y1 + btnH;
        _startOverlay.AddChild(spectateBtn);

        var netPanel = new NetworkPanel { Visible = false };
        _startOverlay.AddChild(netPanel);
        if (Game.Instance?.Net != null)
            netPanel.Init(Game.Instance.Net);

        spectateBtn.Pressed += () => { netPanel.Visible = true; UIAnimator.FadeIn(netPanel, 0.2f); };

        // Hint (bottom center)
        var hint = MkLabel("A/D 变道 · S 掉头 · 空格 爆破 · Shift 冲刺", 18, new Color(0.7f, 0.7f, 0.75f), HorizontalAlignment.Center);
        hint.OffsetLeft = 340f; hint.OffsetTop = 640f;
        hint.OffsetRight = 940f; hint.OffsetBottom = 680f;
        _startOverlay.AddChild(hint);
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
        // help strip stays visible (no auto-hide)

        var p = Game.Instance?.Player;
        if (p != null && IsInstanceValid(p))
        {
            float hp = Mathf.Max(0f, p.Hp);
            _hpBar.MaxValue = p.MaxHp;
            _hpBar.Value = hp;
            _hpText.Text = $"生命 {Mathf.CeilToInt(hp)}";

            // HP bar flash red on damage
            if (hp < _prevHp - 1f && _hpBar.HasThemeStyleboxOverride("fill"))
            {
                var origStyle = _hpBar.GetThemeStylebox("fill") as StyleBoxFlat;
                var flashStyle = SqStyle(new Color(1f, 0.3f, 0.2f), new Color(0f, 0f, 0f));
                _hpBar.AddThemeStyleboxOverride("fill", flashStyle);
                GetTree().CreateTimer(0.2f).Timeout += () => _hpBar.AddThemeStyleboxOverride("fill", SqStyle(Game.Blue, new Color(0f, 0f, 0f)));
            }
            _prevHp = hp;

            float blastCd = 100f * (1f - p.BlastCdTimer / Game.BlastCd);
            float dashCd = 100f * (1f - p.DashCdTimer / Game.DashCd);
            _blastCd.Value = blastCd;
            _dashCd.Value = dashCd;

            // pulse skill bar when it becomes ready
            if (blastCd >= 99f && _prevBlastCd < 99f)
                UIAnimator.Pulse(_blastCd, 1.15f, 0.2f);
            if (dashCd >= 99f && _prevDashCd < 99f)
                UIAnimator.Pulse(_dashCd, 1.15f, 0.2f);
            _prevBlastCd = blastCd;
            _prevDashCd = dashCd;

            _respawn.Text = p.Dead ? $"复活中 {p.RespawnTimer:0.0}s" : "";
        }
    }

    public void Announce(string text, float duration)
    {
        _announce.Text = text;
        _announceT = duration;
        _announce.Modulate = new Color(1f, 0.95f, 0.75f, 1f);
        UIAnimator.SlideIn(_announce, new Vector2(0, -40), 0.3f);
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
        _endScreen.Modulate = new Color(1f, 1f, 1f, 0f);
        UIAnimator.FadeIn(_endScreen, 0.4f);
        UIAnimator.Pulse(_endTitle, 1.12f, 0.4f);
    }
}
