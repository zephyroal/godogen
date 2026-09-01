using Godot;

namespace Xiangqi3D;

/// <summary>Connection UI panel: host or join a networked match.
/// Embeds into the start overlay alongside the single-player buttons.</summary>
public partial class NetworkPanel : Control
{
    private Label _status;
    private LineEdit _ipInput;
    private Button _hostBtn, _joinBtn, _backBtn;
    private NetworkManager _net;

    private static SystemFont UiFont() => new() { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.68f) };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var box = new VBoxContainer();
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddThemeConstantOverride("separation", 18);
        AddChild(box);

        var title = new Label { Text = "联机对战", HorizontalAlignment = Godot.HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", UiFont());
        title.AddThemeFontSizeOverride("font_size", 36);
        title.Modulate = new Color(1f, 0.88f, 0.62f);
        title.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        box.AddChild(title);

        _hostBtn = MkBtn("做主机（等待对手加入）");
        _hostBtn.Pressed += OnHost;
        box.AddChild(_hostBtn);

        _ipInput = new LineEdit
        {
            PlaceholderText = "输入主机 IP 地址",
            CustomMinimumSize = new Vector2(250, 40),
            Text = "127.0.0.1",
        };
        _ipInput.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        box.AddChild(_ipInput);

        _joinBtn = MkBtn("加入主机");
        _joinBtn.Pressed += OnJoin;
        box.AddChild(_joinBtn);

        _status = new Label { Text = "", HorizontalAlignment = Godot.HorizontalAlignment.Center };
        _status.AddThemeFontOverride("font", UiFont());
        _status.AddThemeFontSizeOverride("font_size", 20);
        _status.Modulate = new Color(0.8f, 0.85f, 1f);
        _status.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        box.AddChild(_status);

        _backBtn = MkBtn("返回");
        _backBtn.Pressed += () => { Visible = false; };
        box.AddChild(_backBtn);
    }

    public void Init(NetworkManager net)
    {
        _net = net;
        _net.OnPeerConnected += OnPeerConnected;
        _net.OnPeerDisconnected += OnPeerDisconnected;
    }

    private void OnHost()
    {
        var err = _net.Host();
        _status.Text = err == Error.Ok ? $"主机已创建 · 端口 {NetworkManager.DefaultPort} · 等待对手…" : $"创建失败: {err}";
    }

    private void OnJoin()
    {
        var err = _net.Join(_ipInput.Text.Trim());
        _status.Text = err == Error.Ok ? "连接中…" : $"连接失败: {err}";
    }

    private void OnPeerConnected(long id)
    {
        _status.Text = $"对手已连接 (ID:{id})";
        CallDeferred(nameof(StartOnlineGame));
    }

    private void OnPeerDisconnected(long id)
    {
        _status.Text = "对手已断开";
    }

    private void StartOnlineGame()
    {
        Visible = false;
        Game.Instance?.ChooseMode(_net.IsHost ? Game.Mode.OnlineHost : Game.Mode.OnlineGuest);
    }

    private Button MkBtn(string text)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(250, 60) };
        b.AddThemeFontOverride("font", UiFont());
        b.AddThemeFontSizeOverride("font_size", 22);
        b.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        return b;
    }
}
