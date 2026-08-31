using Godot;

namespace Xiangqi3D;

/// <summary>CanvasLayer UI: mode selection, turn pill, check flash, end screen, undo/restart buttons. Touch-first sizing.</summary>
public partial class HUD : CanvasLayer
{
    private Label _turnPill;
    private Panel _turnBg;
    private Label _checkFlash;
    private Control _startOverlay, _endOverlay;
    private Label _endTitle, _endReason;
    public Button BtnVsAI { get; private set; }
    public Button BtnTwo { get; private set; }
    private float _checkT;

    private static SystemFont UiFont() => new() { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };

    private static StyleBoxFlat Box(Color fill, Color border, int radius = 14)
    {
        return new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
    }

    private static Label MkLabel(string text, int size, Color color, Godot.HorizontalAlignment halign)
    {
        var l = new Label { Text = text, Modulate = color, HorizontalAlignment = halign };
        l.AddThemeFontOverride("font", UiFont());
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    private static Button MkButton(string text, int size)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(240f, 84f) };
        b.AddThemeFontOverride("font", UiFont());
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.16f, 0.2f, 0.3f, 0.95f), new Color(0.5f, 0.6f, 0.8f)));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.22f, 0.28f, 0.42f, 0.95f), new Color(0.6f, 0.7f, 0.9f)));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.12f, 0.15f, 0.22f, 0.95f), new Color(0.4f, 0.5f, 0.7f)));
        return b;
    }

    public override void _Ready()
    {
        // turn pill (top center)
        _turnBg = new Panel
        {
            Position = new Vector2(490f, 16f),
            CustomMinimumSize = new Vector2(300f, 46f),
        };
        _turnBg.AddThemeStyleboxOverride("panel", Box(new Color(0.08f, 0.08f, 0.1f, 0.85f), new Color(0.35f, 0.35f, 0.4f)));
        _turnPill = MkLabel("", 24, new Color(1f, 1f, 1f), Godot.HorizontalAlignment.Center);
        _turnPill.Position = new Vector2(0f, 10f);
        _turnPill.Size = new Vector2(300f, 30f);
        _turnBg.AddChild(_turnPill);
        AddChild(_turnBg);
        SetTurn(Side.Red, false, 1);

        // check flash
        _checkFlash = MkLabel("将军！", 52, new Color(1f, 0.25f, 0.2f), Godot.HorizontalAlignment.Center);
        _checkFlash.Position = new Vector2(0f, 170f);
        _checkFlash.Size = new Vector2(1280f, 70f);
        _checkFlash.Visible = false;
        AddChild(_checkFlash);

        // in-game buttons: 悔棋 | 菜单 | 再来一局
        var undo = MkButton("悔棋 (U)", 24);
        undo.CustomMinimumSize = new Vector2(150f, 70f);
        undo.Position = new Vector2(28f, 620f);
        undo.Pressed += () => Game.Instance?.Undo();
        AddChild(undo);

        var menu = MkButton("菜单", 24);
        menu.CustomMinimumSize = new Vector2(150f, 70f);
        menu.Position = new Vector2(565f, 620f);
        menu.Pressed += () => Game.Instance?.Menu();
        AddChild(menu);

        var reset = MkButton("再来一局", 24);
        reset.CustomMinimumSize = new Vector2(150f, 70f);
        reset.Position = new Vector2(1102f, 620f);
        reset.Pressed += () => Game.Instance?.Rematch();
        AddChild(reset);

        BuildStartOverlay();
        BuildEndOverlay();
    }

    private void BuildStartOverlay()
    {
        _startOverlay = new Control();
        _startOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.66f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _startOverlay.AddChild(dim);

        var title = MkLabel("3D 中国象棋", 64, new Color(1f, 0.92f, 0.7f), Godot.HorizontalAlignment.Center);
        title.Position = new Vector2(0f, 150f);
        title.Size = new Vector2(1280f, 90f);
        _startOverlay.AddChild(title);
        var sub = MkLabel("选择对局模式", 26, new Color(0.85f, 0.85f, 0.9f), Godot.HorizontalAlignment.Center);
        sub.Position = new Vector2(0f, 250f);
        sub.Size = new Vector2(1280f, 40f);
        _startOverlay.AddChild(sub);

        BtnVsAI = MkButton("人机对弈（执红先行）", 30);
        BtnVsAI.Position = new Vector2(430f, 330f);
        _startOverlay.AddChild(BtnVsAI);
        BtnTwo = MkButton("双人对弈（同屏轮流）", 30);
        BtnTwo.Position = new Vector2(430f, 450f);
        _startOverlay.AddChild(BtnTwo);

        var hint = MkLabel("单指点选 · 拖动旋转 · 双指缩放", 20, new Color(0.7f, 0.7f, 0.75f), Godot.HorizontalAlignment.Center);
        hint.Position = new Vector2(0f, 570f);
        hint.Size = new Vector2(1280f, 30f);
        _startOverlay.AddChild(hint);

        BtnVsAI.Pressed += () => { _startOverlay.Visible = false; Game.Instance?.ChooseMode(Game.Mode.VsAI); };
        BtnTwo.Pressed += () => { _startOverlay.Visible = false; Game.Instance?.ChooseMode(Game.Mode.TwoPlayers); };
        AddChild(_startOverlay);
    }

    private void BuildEndOverlay()
    {
        _endOverlay = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _endOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _endOverlay.AddChild(dim);

        _endTitle = MkLabel("", 60, new Color(1f, 0.9f, 0.4f), Godot.HorizontalAlignment.Center);
        _endTitle.Position = new Vector2(0f, 200f);
        _endTitle.Size = new Vector2(1280f, 80f);
        _endOverlay.AddChild(_endTitle);
        _endReason = MkLabel("", 26, new Color(0.9f, 0.9f, 0.95f), Godot.HorizontalAlignment.Center);
        _endReason.Position = new Vector2(0f, 300f);
        _endReason.Size = new Vector2(1280f, 40f);
        _endOverlay.AddChild(_endReason);

        var again = MkButton("再来一局 (R)", 30);
        again.Position = new Vector2(360f, 390f);
        again.Pressed += () => Game.Instance?.Rematch();
        _endOverlay.AddChild(again);

        var toMenu = MkButton("返回菜单", 26);
        toMenu.Position = new Vector2(700f, 400f);
        toMenu.Pressed += () => Game.Instance?.Menu();
        _endOverlay.AddChild(toMenu);
        AddChild(_endOverlay);
    }

    public void HideStart() => _startOverlay.Visible = false;

    public override void _Process(double delta)
    {
        if (_checkT > 0f)
        {
            _checkT -= (float)delta;
            _checkFlash.Modulate = new Color(1f, 0.3f, 0.25f, Mathf.Min(1f, _checkT / 0.4f));
            if (_checkT <= 0f) _checkFlash.Visible = false;
        }
    }

    public void SetTurn(Side? turn, bool thinking, int moveNumber)
    {
        if (turn == null)
        {
            _turnPill.Text = "对局结束";
            _turnPill.Modulate = new Color(0.8f, 0.8f, 0.8f);
            return;
        }
        bool red = turn == Side.Red;
        string text = thinking ? "黑方思考中…" : red ? "红方行棋" : "黑方行棋";
        _turnPill.Text = moveNumber > 0 ? $"第 {moveNumber} 手 · {text}" : text;
        _turnPill.Modulate = red ? new Color(1f, 0.55f, 0.45f) : new Color(0.75f, 0.85f, 1f);
    }

    public void FlashCheck()
    {
        _checkT = 1.6f;
        _checkFlash.Visible = true;
        _checkFlash.Modulate = new Color(1f, 0.3f, 0.25f, 1f);
    }

    public void ShowEnd(Side winner, bool checkmate)
    {
        _endTitle.Text = winner == Side.Red ? "红方胜利！" : "黑方胜利！";
        _endTitle.Modulate = winner == Side.Red ? new Color(1f, 0.5f, 0.4f) : new Color(0.7f, 0.8f, 1f);
        _endReason.Text = checkmate ? "绝杀 —— 被将死" : "困毙 —— 无子可动";
        _endOverlay.Visible = true;
    }
}
