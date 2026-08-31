using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>CanvasLayer UI: fortress score squares, HP, skill cooldowns, announcements, end screen.</summary>
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

    public override void _Ready()
    {
        // tug-of-war top bar: 蓝方X/10 — coin — 红方X/10 (concept art HUD)
        var top = new HBoxContainer { Position = new Vector2(285f, 12f) };
        top.AddThemeConstantOverride("separation", 10);
        AddChild(top);

        _blueScore = MkScoreBar(leftToRight: true);
        _blueScoreText = MkLabel("蓝方 0/10", 18, new Color(1f, 1f, 1f), HorizontalAlignment.Center);
        _blueScoreText.Position = new Vector2(0f, 6f);
        _blueScoreText.Size = new Vector2(330f, 24f);
        var blueSide = new Control { CustomMinimumSize = new Vector2(330f, 28f) };
        blueSide.AddChild(_blueScore);
        blueSide.AddChild(_blueScoreText);
        top.AddChild(blueSide);

        var coin = new Panel { CustomMinimumSize = new Vector2(32f, 32f) };
        coin.AddThemeStyleboxOverride("panel", SqStyle(new Color(1f, 0.82f, 0.2f), new Color(0.8f, 0.6f, 0.1f)));
        top.AddChild(coin);

        _redScore = MkScoreBar(leftToRight: false);
        _redScoreText = MkLabel("红方 0/10", 18, new Color(1f, 1f, 1f), HorizontalAlignment.Center);
        _redScoreText.Position = new Vector2(0f, 6f);
        _redScoreText.Size = new Vector2(330f, 24f);
        var redSide = new Control { CustomMinimumSize = new Vector2(330f, 28f) };
        redSide.AddChild(_redScore);
        redSide.AddChild(_redScoreText);
        top.AddChild(redSide);
        UpdateFortressSquares();

        // announcements
        _announce = MkLabel("", 34, new Color(1f, 0.95f, 0.75f), HorizontalAlignment.Center);
        _announce.Position = new Vector2(0f, 64f);
        _announce.Size = new Vector2(1280f, 50f);
        AddChild(_announce);

        // help
        _help = MkLabel("A/D 变道 · S 掉头防守 · 空格 爆破 · Shift 冲刺撞击", 22, new Color(1f, 1f, 1f, 0.85f), HorizontalAlignment.Center);
        _help.Position = new Vector2(0f, 630f);
        _help.Size = new Vector2(1280f, 34f);
        AddChild(_help);
        _helpT = 12f;

        // HP bar bottom-left
        _hpBar = new ProgressBar
        {
            MinValue = 0, MaxValue = Game.PlayerHp, Value = Game.PlayerHp,
            CustomMinimumSize = new Vector2(300f, 24f),
            Position = new Vector2(24f, 640f),
            ShowPercentage = false,
        };
        _hpBar.AddThemeStyleboxOverride("background", SqStyle(new Color(0.1f, 0.1f, 0.12f), new Color(0f, 0f, 0f)));
        _hpBar.AddThemeStyleboxOverride("fill", SqStyle(Game.Blue, new Color(0f, 0f, 0f)));
        AddChild(_hpBar);
        _hpText = MkLabel("生命 120", 20, new Color(1f, 1f, 1f));
        _hpText.Position = new Vector2(28f, 612f);
        AddChild(_hpText);

        // skill cooldowns bottom-right
        _blastCd = MkSkillBar(980f, "爆破 [空格]", new Color(1f, 0.6f, 0.2f));
        _dashCd = MkSkillBar(1105f, "冲刺 [Shift]", new Color(0.5f, 0.8f, 1f));

        // respawn overlay
        _respawn = MkLabel("", 40, new Color(1f, 0.4f, 0.4f), HorizontalAlignment.Center);
        _respawn.Position = new Vector2(0f, 330f);
        _respawn.Size = new Vector2(1280f, 60f);
        AddChild(_respawn);

        // end screen
        _endScreen = new Control
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _endScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _endScreen.AddChild(dim);
        var box = new VBoxContainer { Position = new Vector2(340f, 200f) };
        box.AddThemeConstantOverride("separation", 18);
        _endScreen.AddChild(box);

        _endTitle = MkLabel("胜利！", 64, new Color(1f, 0.9f, 0.3f), HorizontalAlignment.Center);
        _endTitle.Size = new Vector2(600f, 80f);
        box.AddChild(_endTitle);
        _endList = MkLabel("", 22, new Color(0.9f, 0.9f, 0.9f), HorizontalAlignment.Center);
        _endList.Size = new Vector2(600f, 140f);
        box.AddChild(_endList);
        var hint = MkLabel("按 R 重新开始", 26, new Color(1f, 1f, 1f), HorizontalAlignment.Center);
        hint.Size = new Vector2(600f, 40f);
        box.AddChild(hint);
        AddChild(_endScreen);
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

    private ProgressBar MkSkillBar(float x, string title, Color color)
    {
        var lbl = MkLabel(title, 20, new Color(1f, 1f, 1f));
        lbl.Position = new Vector2(x, 612f);
        lbl.Size = new Vector2(120f, 26f);
        AddChild(lbl);
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = 100,
            CustomMinimumSize = new Vector2(120f, 14f),
            Position = new Vector2(x, 640f),
            ShowPercentage = false,
        };
        bar.AddThemeStyleboxOverride("background", SqStyle(new Color(0.1f, 0.1f, 0.12f), new Color(0f, 0f, 0f)));
        bar.AddThemeStyleboxOverride("fill", SqStyle(color, new Color(0f, 0f, 0f)));
        AddChild(bar);
        return bar;
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
