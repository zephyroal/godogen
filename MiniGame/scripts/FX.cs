using Godot;

namespace FortressRush;

/// <summary>Static FX helpers shared by the game.</summary>
public static class FX
{
    private static BoxMesh _debris;
    private static SystemFont _font;

    /// <summary>One-shot cube debris burst. Caller adds the node to the tree.</summary>
    public static CpuParticles3D BlockBurst(Vector3 pos, Color color, int amount)
    {
        _debris ??= new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.5f) };
        var p = new CpuParticles3D
        {
            Position = pos,
            Amount = amount,
            Lifetime = 0.7f,
            OneShot = true,
            Explosiveness = 1f,
            Emitting = true,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 80f,
            Gravity = new Vector3(0f, -14f, 0f),
            InitialVelocityMin = 3f,
            InitialVelocityMax = 7f,
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.1f,
            Mesh = _debris,
            Color = color,
        };
        return p;
    }

    public static SystemFont UiFont()
    {
        if (_font == null)
            _font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };
        return _font;
    }
}

/// <summary>Expanding glowing sphere + debris ring for the blast skill; self-frees.</summary>
public partial class BlastFx : Node3D
{
    private MeshInstance3D _sphere;
    private float _t;
    private readonly float _radius;

    public BlastFx(Vector3 pos, float radius)
    {
        Position = pos;
        _radius = radius;
    }

    public override void _Ready()
    {
        _sphere = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 20, Rings = 12 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.85f, 0.45f, 0.7f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.7f, 0.3f),
                EmissionEnergyMultiplier = 2.5f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Scale = Vector3.One * 0.2f,
        };
        AddChild(_sphere);

        var debris = FX.BlockBurst(Vector3.Zero, new Color(1f, 0.75f, 0.35f), 18);
        AddChild(debris);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float k = Mathf.Min(1f, _t / 0.28f);
        _sphere.Scale = Vector3.One * Mathf.Lerp(0.2f, _radius * 0.9f, k);
        if (_t > 0.28f)
        {
            float fade = 1f - (_t - 0.28f) / 0.25f;
            if (fade <= 0f)
            {
                QueueFree();
                return;
            }
            if (_sphere.MaterialOverride is StandardMaterial3D m)
                m.AlbedoColor = new Color(1f, 0.85f, 0.45f, 0.7f * fade);
        }
    }
}
