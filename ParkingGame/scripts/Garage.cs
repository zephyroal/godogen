using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace ParkingGame;

public record SkinDef(string Id, string Name, int Price, Color Paint);

/// <summary>Coins and paint skins, persisted to user://parking_save.json.
/// Successful parks pay out (perfect costs more); skins are bought once and
/// kept forever.</summary>
public static class Garage
{
    public const int PerfectReward = 300;
    public const int SuccessReward = 150;

    public static readonly SkinDef[] Skins =
    {
        new("classic", "经典红", 0, new Color(0.82f, 0.27f, 0.24f)),
        new("onyx", "曜石黑", 200, new Color(0.08f, 0.08f, 0.09f)),
        new("ocean", "海洋蓝", 300, new Color(0.16f, 0.35f, 0.75f)),
        new("pearl", "珍珠白", 400, new Color(0.92f, 0.92f, 0.90f)),
        new("neon", "荧光绿", 600, new Color(0.25f, 1.00f, 0.35f)),
        new("gold", "鎏金", 1000, new Color(1.00f, 0.78f, 0.25f)),
    };

    // ---- persistence ----
    private sealed class SaveData
    {
        public int Coins { get; set; }
        public List<string> Owned { get; set; } = new();
        public string Selected { get; set; } = "classic";
    }

    private static SaveData _data = Load();
    private static string SavePath => ProjectSettings.GlobalizePath("user://parking_save.json");

    private static SaveData Load()
    {
        try
        {
            if (File.Exists(SavePath))
                return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath)) ?? new SaveData();
        }
        catch { /* corrupt save — start fresh */ }
        return new SaveData();
    }

    private static void Save() => File.WriteAllText(SavePath, JsonSerializer.Serialize(_data));

    // ---- state ----
    public static int Coins => _data.Coins;
    public static bool Owns(string id) => id == "classic" || _data.Owned.Contains(id);
    public static string SelectedId => _data.Selected;
    public static Color SelectedPaint => FindSkin(_data.Selected)?.Paint ?? Skins[0].Paint;
    public static SkinDef FindSkin(string id) => System.Array.Find(Skins, s => s.Id == id);

    public static void Award(int coins)
    {
        _data.Coins += coins;
        Save();
    }

    public static bool TryBuy(string id)
    {
        var s = FindSkin(id);
        if (s == null || Owns(id) || _data.Coins < s.Price) return false;
        _data.Coins -= s.Price;
        _data.Owned.Add(id);
        Save();
        return true;
    }

    public static bool TrySelect(string id)
    {
        if (!Owns(id)) return false;
        _data.Selected = id;
        Save();
        return true;
    }
}

/// <summary>Garage overlay: swatches + buy/select buttons for every skin.
/// Opened with B from the start screen or after a verdict.</summary>
public partial class GarageUI : CanvasLayer
{
    private readonly Car _car;
    private Control _panel = null!;
    private Label _title = null!;
    private VBoxContainer _rows = null!;

    public GarageUI(Car car) => _car = car;

    private static Label MkLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text, Modulate = color };
        l.AddThemeFontOverride("font", new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" },
        });
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    private static Button MkButton(string text, bool enabled)
    {
        var b = new Button { Text = text, Disabled = !enabled, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontOverride("font", new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" },
        });
        b.AddThemeFontSizeOverride("font_size", 18);
        return b;
    }

    public override void _Ready()
    {
        _panel = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel.AddChild(dim);

        var box = new VBoxContainer();
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        box.AddThemeConstantOverride("separation", 12);
        _title = MkLabel("车库", 34, new Color(1f, 0.88f, 0.6f));
        _title.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        box.AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 8);
        box.AddChild(_rows);

        var close = MkButton("关闭 (B)", true);
        close.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        close.Pressed += Close;
        box.AddChild(close);

        _panel.AddChild(box);
        AddChild(_panel);
    }

    public bool IsOpen => _panel.Visible;
    public void Open() { _panel.Visible = true; Rebuild(); }
    public void Close() => _panel.Visible = false;

    private void Rebuild()
    {
        foreach (var c in _rows.GetChildren())
            c.QueueFree();

        _title.Text = $"车库　·　金币 {Garage.Coins}";
        foreach (var s in Garage.Skins)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);
            row.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

            var swatch = new ColorRect { Color = s.Paint, CustomMinimumSize = new Vector2(56, 32) };
            row.AddChild(swatch);
            row.AddChild(MkLabel(s.Name, 22, new Color(0.95f, 0.92f, 0.86f)));

            string status;
            Button btn;
            if (Garage.SelectedId == s.Id)
            {
                status = "已装备";
                btn = MkButton(status, false);
            }
            else if (Garage.Owns(s.Id))
            {
                btn = MkButton("选择", true);
                btn.Pressed += () => { Garage.TrySelect(s.Id); ApplyAndRebuild(s); };
            }
            else
            {
                status = $"{s.Price} 金币";
                btn = MkButton(Garage.Coins >= s.Price ? $"购买 {status}" : status, Garage.Coins >= s.Price);
                btn.Pressed += () => { if (Garage.TryBuy(s.Id)) { Garage.TrySelect(s.Id); ApplyAndRebuild(s); } };
            }
            row.AddChild(btn);
            _rows.AddChild(row);
        }
    }

    private void ApplyAndRebuild(SkinDef s)
    {
        _car.ApplySkin(Garage.SelectedPaint);
        Rebuild();
    }
}
