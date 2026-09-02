using Godot;

namespace Xiangqi3D;

/// <summary>CanvasLayer UI: mode selection, turn pill, check flash, end screen, undo/restart buttons.
/// Every element is anchor/container laid out so it adapts to any window size; touch-first sizing (buttons ≥72px).</summary>
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
    private Color _hpBarOrigColor;
    private bool _turnPulseQueued;

    // warm lacquer palette matching the wooden board
    private static readonly Color PanelBg = new(0.13f, 0.08f, 0.05f, 0.92f);
    private static readonly Color PanelBorder = new(0.55f, 0.42f, 0.24f);
    private static readonly Color GoldBorder = new(0.85f, 0.68f, 0.38f);
    private static readonly Color Gold = new(1f, 0.88f, 0.62f);
    private static readonly Color Cream = new(0.93f, 0.88f, 0.80f);
    private static readonly Color BtnBg = new(0.21f, 0.13f, 0.08f, 0.95f);
    private static readonly Color BtnBgHover = new(0.29f, 0.18f, 0.11f, 0.97f);
    private static readonly Color BtnBgPressed = new(0.15f, 0.09f, 0.06f, 0.97f);

    private static SystemFont UiFont() => new() { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };

    private static StyleBoxFlat Box(Color fill, Color border, int radius = 12)
    {
        return new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 10, ContentMarginBottom = 10,
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

    private static Button MkButton(string text, int size)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(250f, 84f) };
        b.AddThemeFontOverride("font", UiFont());
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeColorOverride("font_color", Cream);
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.AddThemeColorOverride("font_pressed_color", Gold);
        b.AddThemeColorOverride("font_focus_color", Gold);
        b.AddThemeColorOverride("font_disabled_color", new Color(0.55f, 0.5f, 0.45f));
        b.AddThemeStyleboxOverride("normal", Box(BtnBg, PanelBorder));
        b.AddThemeStyleboxOverride("hover", Box(BtnBgHover, GoldBorder));
        b.AddThemeStyleboxOverride("pressed", Box(BtnBgPressed, new Color(0.45f, 0.34f, 0.2f)));
        b.AddThemeStyleboxOverride("focus", Box(BtnBgHover, GoldBorder));
        return b;
    }

    public override void _Ready()
    {
        // turn pill (anchored top center)
        _turnBg = new Panel();
        Anchor(_turnBg, Control.LayoutPreset.CenterTop, -160f, 14f, 160f, 62f);
        _turnBg.AddThemeStyleboxOverride("panel", Box(PanelBg, PanelBorder, 22));
        _turnPill = MkLabel("", 24, new Color(1f, 1f, 1f), Godot.HorizontalAlignment.Center);
        _turnPill.VerticalAlignment = VerticalAlignment.Center;
        _turnPill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _turnBg.AddChild(_turnPill);
        AddChild(_turnBg);
        SetTurn(Side.Red, false, 1);

        // check flash (upper center)
        _checkFlash = Shadowed(MkLabel("将军！", 54, new Color(1f, 0.32f, 0.24f), Godot.HorizontalAlignment.Center),
            new Color(0.2f, 0f, 0f, 0.85f));
        Anchor(_checkFlash, Control.LayoutPreset.CenterTop, -320f, 150f, 320f, 230f);
        _checkFlash.Visible = false;
        AddChild(_checkFlash);

        // in-game buttons: 悔棋 | 菜单 | 再来一局 (anchored bottom row)
        var undo = MkButton("悔棋 (U)", 24);
        undo.CustomMinimumSize = new Vector2(150f, 74f);
        Anchor(undo, Control.LayoutPreset.BottomLeft, 24f, -98f, 174f, -24f);
        undo.Pressed += () => Game.Instance?.Undo();
        AddChild(undo);

        var menu = MkButton("菜单", 24);
        menu.CustomMinimumSize = new Vector2(150f, 74f);
        Anchor(menu, Control.LayoutPreset.TopRight, -174f, 14f, -24f, 88f);
        // ghost style: clearly secondary, top-right so it never covers the board
        menu.AddThemeStyleboxOverride("normal", Box(new Color(0.1f, 0.06f, 0.04f, 0.55f), new Color(0.4f, 0.31f, 0.19f, 0.75f)));
        menu.AddThemeStyleboxOverride("hover", Box(new Color(0.2f, 0.12f, 0.07f, 0.82f), GoldBorder));
        menu.AddThemeStyleboxOverride("pressed", Box(new Color(0.1f, 0.06f, 0.04f, 0.85f), new Color(0.4f, 0.31f, 0.19f)));
        menu.AddThemeStyleboxOverride("focus", Box(new Color(0.2f, 0.12f, 0.07f, 0.82f), GoldBorder));
        menu.Pressed += () => Game.Instance?.Menu();
        AddChild(menu);

        var reset = MkButton("再来一局", 24);
        reset.CustomMinimumSize = new Vector2(150f, 74f);
        Anchor(reset, Control.LayoutPreset.BottomRight, -174f, -98f, -24f, -24f);
        reset.Pressed += () => Game.Instance?.Rematch();
        AddChild(reset);

        BuildStartOverlay();
        BuildEndOverlay();
    }

    private void BuildStartOverlay()
    {
        _startOverlay = new Control();
        _startOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _startOverlay.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_startOverlay);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.68f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _startOverlay.AddChild(dim);

        // Title
        var title = Shadowed(MkLabel("3D 中国象棋", 64, Gold, Godot.HorizontalAlignment.Center),
            new Color(0f, 0f, 0f, 0.75f));
        title.OffsetLeft = 240f; title.OffsetTop = 60f;
        title.OffsetRight = 1040f; title.OffsetBottom = 150f;
        _startOverlay.AddChild(title);

        var sub = MkLabel("选择对局模式", 24, Cream, Godot.HorizontalAlignment.Center);
        sub.OffsetLeft = 340f; sub.OffsetTop = 155f;
        sub.OffsetRight = 940f; sub.OffsetBottom = 195f;
        _startOverlay.AddChild(sub);

        // Three buttons: all anchors = 0 (top-left origin), offsets define rect
        float btnW = 300f, btnH = 72f, gap = 14f;
        float btnX = (1280 - btnW) / 2f;
        float y0 = 230f;

        BtnVsAI = MkButton("人机对弈（执红先行）", 24);
        BtnVsAI.CustomMinimumSize = new Vector2(btnW, btnH);
        BtnVsAI.OffsetLeft = btnX; BtnVsAI.OffsetTop = y0;
        BtnVsAI.OffsetRight = btnX + btnW; BtnVsAI.OffsetBottom = y0 + btnH;
        _startOverlay.AddChild(BtnVsAI);

        float y1 = y0 + btnH + gap;
        BtnTwo = MkButton("双人对弈（同屏轮流）", 24);
        BtnTwo.CustomMinimumSize = new Vector2(btnW, btnH);
        BtnTwo.OffsetLeft = btnX; BtnTwo.OffsetTop = y1;
        BtnTwo.OffsetRight = btnX + btnW; BtnTwo.OffsetBottom = y1 + btnH;
        _startOverlay.AddChild(BtnTwo);

        float y2 = y1 + btnH + gap;
        var btnOnline = MkButton("联机对战", 24);
        btnOnline.CustomMinimumSize = new Vector2(btnW, btnH);
        btnOnline.OffsetLeft = btnX; btnOnline.OffsetTop = y2;
        btnOnline.OffsetRight = btnX + btnW; btnOnline.OffsetBottom = y2 + btnH;
        _startOverlay.AddChild(btnOnline);

        // Hint text (bottom center)
        var hint = MkLabel("单指点选 · 拖动旋转 · 双指缩放", 20, new Color(0.78f, 0.71f, 0.6f), Godot.HorizontalAlignment.Center);
        hint.OffsetLeft = 340f; hint.OffsetTop = 640f;
        hint.OffsetRight = 940f; hint.OffsetBottom = 680f;
        _startOverlay.AddChild(hint);

        BtnVsAI.Pressed += () => { UIAnimator.FadeOut(_startOverlay, 0.2f, true); Game.Instance?.ChooseMode(Game.Mode.VsAI); };
        BtnTwo.Pressed += () => { UIAnimator.FadeOut(_startOverlay, 0.2f, true); Game.Instance?.ChooseMode(Game.Mode.TwoPlayers); };

        var netPanel = new NetworkPanel { Visible = false };
        _startOverlay.AddChild(netPanel);
        netPanel.Init(Game.Instance.Net);

        btnOnline.Pressed += () => { netPanel.Visible = true; UIAnimator.FadeIn(netPanel, 0.2f); };

        // visible immediately — StaggerIn disabled to avoid capture timing issues

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
        box.AddThemeConstantOverride("separation", 24);
        _endOverlay.AddChild(box);

        _endTitle = Shadowed(MkLabel("", 62, new Color(1f, 0.9f, 0.45f), Godot.HorizontalAlignment.Center),
            new Color(0f, 0f, 0f, 0.8f));
        _endTitle.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(_endTitle);
        _endReason = MkLabel("", 26, Cream, Godot.HorizontalAlignment.Center);
        _endReason.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(_endReason);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        row.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        var again = MkButton("再来一局 (R)", 30);
        again.Pressed += () => Game.Instance?.Rematch();
        var toMenu = MkButton("返回菜单", 26);
        toMenu.Pressed += () => Game.Instance?.Menu();
        row.AddChild(again);
        row.AddChild(toMenu);
        box.AddChild(row);
        AddChild(_endOverlay);
    }

    public void HideStart() => _startOverlay.Visible = false;
    public bool IsStartVisible => _startOverlay?.Visible ?? false;

    public override void _Process(double delta)
    {
        if (_turnPulseQueued)
        {
            _turnPulseQueued = false;
            UIAnimator.Pulse(_turnBg, 1.05f, 0.2f);
        }
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
        _turnPill.Modulate = red ? new Color(1f, 0.6f, 0.5f) : new Color(0.8f, 0.88f, 1f);
        _turnPulseQueued = true;
    }

    public void FlashCheck()
    {
        _checkT = 1.6f;
        _checkFlash.Visible = true;
        _checkFlash.Modulate = new Color(1f, 0.3f, 0.25f, 1f);
        UIAnimator.Shake(_checkFlash, 10f, 0.35f);
    }

    public void ShowEnd(Side winner, bool checkmate)
    {
        _endTitle.Text = winner == Side.Red ? "红方胜利！" : "黑方胜利！";
        _endTitle.Modulate = winner == Side.Red ? new Color(1f, 0.5f, 0.4f) : new Color(0.95f, 0.83f, 0.55f);
        _endReason.Text = checkmate ? "绝杀 —— 被将死" : "困毙 —— 无子可动";
        _endOverlay.Visible = true;
        UIAnimator.FadeIn(_endOverlay, 0.4f);
        _endTitle.Modulate = new Color(_endTitle.Modulate, 0f);
        UIAnimator.SlideIn(_endTitle, new Vector2(0, -60), 0.5f);
    }
}
