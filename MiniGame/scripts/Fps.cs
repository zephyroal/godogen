using Godot;

namespace FortressRush;

/// <summary>
/// Debug-only FPS readout pinned to the top-right corner. A repeating Timer refreshes the
/// label twice a second, so the indicator adds no per-frame work; F3 toggles it and the
/// timer stops while hidden. Release builds never create the node (see Game._Ready).
/// </summary>
public partial class Fps : CanvasLayer
{
    private Label _label;
    private Timer _timer;

    public override void _Ready()
    {
        Layer = 10; // above the HUD canvas
        _label = new Label();
        _label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _label.GrowHorizontal = Control.GrowDirection.Begin;
        _label.OffsetLeft = -150f;
        _label.OffsetTop = 6f;
        _label.OffsetRight = -6f;
        _label.OffsetBottom = 34f;
        _label.AddThemeFontOverride("font", FX.UiFont());
        _label.AddThemeFontSizeOverride("font_size", 20);
        _label.AddThemeColorOverride("font_color", new Color(0.85f, 1f, 0.85f));
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
