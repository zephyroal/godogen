using Godot;

namespace FortressRush;

/// <summary>Connection UI panel: host or join a networked spectated match.
/// Uses absolute offsets (anchor=0,0,0,0) for reliable layout at 1280×720.</summary>
public partial class NetworkPanel : Control
{
    private Label _status;
    private LineEdit _ipInput;
    private Button _hostBtn, _joinBtn, _backBtn;
    private NetworkManager _net;

    private static SystemFont UiFont() => FX.UiFont();

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.68f) };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        // Title
        var title = new Label { Text = "联机观战", HorizontalAlignment = Godot.HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", UiFont());
        title.AddThemeFontSizeOverride("font_size", 36);
        title.Modulate = new Color(1f, 0.88f, 0.62f);
        title.OffsetLeft = 390f; title.OffsetTop = 150f;
        title.OffsetRight = 890f; title.OffsetBottom = 210f;
        AddChild(title);

        // Host button
        _hostBtn = new Button { Text = "做主机（开始游戏，对手观战）", CustomMinimumSize = new Vector2(340, 60) };
        _hostBtn.AddThemeFontOverride("font", UiFont());
        _hostBtn.AddThemeFontSizeOverride("font_size", 22);
        _hostBtn.OffsetLeft = 470f; _hostBtn.OffsetTop = 250f;
        _hostBtn.OffsetRight = 810f; _hostBtn.OffsetBottom = 310f;
        _hostBtn.Pressed += OnHost;
        AddChild(_hostBtn);

        // IP input
        _ipInput = new LineEdit
        {
            PlaceholderText = "输入主机 IP 地址",
            Text = "127.0.0.1",
            CustomMinimumSize = new Vector2(300, 40),
        };
        _ipInput.AddThemeFontOverride("font", UiFont());
        _ipInput.AddThemeFontSizeOverride("font_size", 20);
        _ipInput.OffsetLeft = 490f; _ipInput.OffsetTop = 340f;
        _ipInput.OffsetRight = 790f; _ipInput.OffsetBottom = 380f;
        AddChild(_ipInput);

        // Join button
        _joinBtn = new Button { Text = "加入观战", CustomMinimumSize = new Vector2(300, 60) };
        _joinBtn.AddThemeFontOverride("font", UiFont());
        _joinBtn.AddThemeFontSizeOverride("font_size", 22);
        _joinBtn.OffsetLeft = 490f; _joinBtn.OffsetTop = 400f;
        _joinBtn.OffsetRight = 790f; _joinBtn.OffsetBottom = 460f;
        _joinBtn.Pressed += OnJoin;
        AddChild(_joinBtn);

        // Status
        _status = new Label { Text = "", HorizontalAlignment = Godot.HorizontalAlignment.Center };
        _status.AddThemeFontOverride("font", UiFont());
        _status.AddThemeFontSizeOverride("font_size", 20);
        _status.Modulate = new Color(0.8f, 0.85f, 1f);
        _status.OffsetLeft = 390f; _status.OffsetTop = 480f;
        _status.OffsetRight = 890f; _status.OffsetBottom = 520f;
        AddChild(_status);

        // Back button
        _backBtn = new Button { Text = "返回", CustomMinimumSize = new Vector2(150, 50) };
        _backBtn.AddThemeFontOverride("font", UiFont());
        _backBtn.AddThemeFontSizeOverride("font_size", 20);
        _backBtn.OffsetLeft = 565f; _backBtn.OffsetTop = 550f;
        _backBtn.OffsetRight = 715f; _backBtn.OffsetBottom = 600f;
        _backBtn.Pressed += () => { Visible = false; };
        AddChild(_backBtn);
    }

    public void Init(NetworkManager net)
    {
        if (net == null) return;
        _net = net;
        _net.OnPeerConnected += OnPeerConnected;
        _net.OnPeerDisconnected += OnPeerDisconnected;
        _net.OnDisconnected += OnDisconnected;
    }

    private void OnHost()
    {
        if (_net == null) return;
        var err = _net.Host();
        _status.Text = err == Error.Ok ? $"主机已创建 · 端口 {NetworkManager.DefaultPort} · 开始游戏，等待观战者…" : $"创建失败: {err}";
        if (err == Error.Ok)
        {
            Visible = false;
            Game.Instance?.ChooseMode(GameMode.SpectatorHost);
        }
    }

    private void OnJoin()
    {
        if (_net == null) return;
        var err = _net.Join(_ipInput.Text.Trim());
        _status.Text = err == Error.Ok ? "连接中…" : $"连接失败: {err}";
    }

    private void OnPeerConnected(long id)
    {
        _status.Text = $"观战者已连接 (ID:{id})";
    }

    private void OnPeerDisconnected(long id)
    {
        _status.Text = "观战者已断开";
    }

    private void OnDisconnected()
    {
        _status.Text = "连接已断开";
    }
}
