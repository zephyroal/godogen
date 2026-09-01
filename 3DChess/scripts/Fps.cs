using Godot;

namespace Xiangqi3D;

/// <summary>
/// Debug-only FPS readout pinned to the top-left corner. A repeating Timer refreshes the
/// label twice a second, so the indicator adds no per-frame work; F3 toggles it and the
/// timer stops while hidden. Release builds never create the node (see Game._Ready).
/// </summary>
public partial class Fps : CanvasLayer
{
    private static SystemFont _font;

    private Label _label;
    private Timer _timer;

    public override void _Ready()
    {
        Layer = 10; // above the HUD canvas
        if (_font == null)
            _font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };

        _label = new Label();
        _label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        _label.OffsetLeft = 16f;
        _label.OffsetTop = 12f;
        _label.OffsetRight = 150f;
        _label.OffsetBottom = 40f;
        _label.AddThemeFontOverride("font", _font);
        _label.AddThemeFontSizeOverride("font_size", 20);
        _label.AddThemeColorOverride("font_color", new Color(0.93f, 0.88f, 0.80f)); // HUD cream
        _label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.7f));
        _label.AddThemeConstantOverride("shadow_offset_x", 2);
        _label.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(_label);

        _timer = new Timer { WaitTime = 0.5, Autostart = true };
        _timer.Timeout += Refresh;
        AddChild(_timer);
        Refresh();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } k && k.PhysicalKeycode == Key.F3)
        {
            Visible = !Visible;
            if (Visible)
            {
                _timer.Start();
                Refresh();
            }
            else _timer.Stop();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Refresh()
    {
        _label.Text = $"FPS {Engine.GetFramesPerSecond():0}";
    }
}
