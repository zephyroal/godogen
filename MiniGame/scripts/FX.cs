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

/// <summary>Expanding glowing sphere + debris ring + forward laser for the blast skill; self-frees.</summary>
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
                EmissionEnergyMultiplier = 3.5f,
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

/// <summary>Energy laser beam from origin to target; flashes bright then fades. Self-frees.</summary>
public partial class LaserFx : Node3D
{
    private MeshInstance3D _beam;
    private StandardMaterial3D _mat;
    private float _t;
    private readonly Vector3 _from, _to;

    public LaserFx(Vector3 from, Vector3 to, Color color)
    {
        _from = from;
        _to = to;
        Position = (from + to) * 0.5f;
    }

    public override void _Ready()
    {
        var dir = _to - _from;
        float len = dir.Length();
        _mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 0.9f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.9f, 0.5f),
            EmissionEnergyMultiplier = 5f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _beam = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.3f, len), Material = _mat },
        };
        // orient the beam along the direction vector
        if (len > 0.01f)
            _beam.LookAt(_to - Position, Vector3.Up);
        AddChild(_beam);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t < 0.06f)
        {
            // initial flash: thickest, brightest
            float k = _t / 0.06f;
            _beam.Scale = new Vector3(Mathf.Lerp(1.5f, 1f, k), Mathf.Lerp(1.5f, 1f, k), 1f);
            _mat.EmissionEnergyMultiplier = Mathf.Lerp(6f, 4f, k);
        }
        else
        {
            float fade = 1f - (_t - 0.06f) / 0.18f;
            if (fade <= 0f)
            {
                QueueFree();
                return;
            }
            float w = Mathf.Lerp(1f, 0.15f, 1f - fade);
            _beam.Scale = new Vector3(w, w, 1f);
            _mat.AlbedoColor = new Color(1f, 0.9f, 0.5f, 0.9f * fade);
            _mat.EmissionEnergyMultiplier = 4f * fade;
        }
    }
}

/// <summary>Impact spark burst + expanding shockwave ring when a runner is hit. Self-frees.</summary>
public partial class HitSparkFx : Node3D
{
    private MeshInstance3D _ring;
    private StandardMaterial3D _ringMat;
    private float _t;
    private readonly Color _color;

    public HitSparkFx(Vector3 pos, Color color)
    {
        Position = pos;
        _color = color;
    }

    public override void _Ready()
    {
        // sparks
        var sparks = new CpuParticles3D
        {
            Amount = 14,
            Lifetime = 0.4f,
            OneShot = true,
            Explosiveness = 1f,
            Emitting = true,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 120f,
            Gravity = new Vector3(0f, -10f, 0f),
            InitialVelocityMin = 4f,
            InitialVelocityMax = 9f,
            ScaleAmountMin = 0.3f,
            ScaleAmountMax = 0.6f,
            Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.2f, 0.2f) },
            Color = _color.Lightened(0.3f),
        };
        AddChild(sparks);

        // shockwave ring (flat torus on the ground)
        _ringMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 0.8f),
            EmissionEnabled = true,
            Emission = _color.Lightened(0.4f),
            EmissionEnergyMultiplier = 3f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
        };
        _ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.2f, OuterRadius = 0.35f, Material = _ringMat },
            RotationDegrees = new Vector3(90f, 0f, 0f),
            Position = new Vector3(0f, 0.1f, 0f),
        };
        AddChild(_ring);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float k = Mathf.Min(1f, _t / 0.3f);
        _ring.Scale = Vector3.One * Mathf.Lerp(1f, 4f, k);
        float fade = 1f - k;
        if (fade <= 0f)
        {
            QueueFree();
            return;
        }
        _ringMat.AlbedoColor = new Color(1f, 1f, 1f, 0.8f * fade);
        _ringMat.EmissionEnergyMultiplier = 3f * fade;
    }
}

/// <summary>Stretching afterimage trail behind a dashing runner. Self-frees.</summary>
public partial class DashTrailFx : Node3D
{
    private MeshInstance3D _trail;
    private StandardMaterial3D _mat;
    private float _t;
    private readonly Color _color;

    public DashTrailFx(Vector3 pos, Color color)
    {
        Position = pos;
        _color = color;
    }

    public override void _Ready()
    {
        _mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(_color.R, _color.G, _color.B, 0.5f),
            EmissionEnabled = true,
            Emission = _color,
            EmissionEnergyMultiplier = 2f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
        };
        _trail = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 2.4f, 0.3f), Material = _mat },
            Position = new Vector3(0f, 1.2f, 0f),
        };
        AddChild(_trail);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float fade = 1f - _t / 0.3f;
        if (fade <= 0f)
        {
            QueueFree();
            return;
        }
        _trail.Scale = new Vector3(1f, 1f, 1f + _t * 8f);
        _mat.AlbedoColor = new Color(_color.R, _color.G, _color.B, 0.5f * fade);
        _mat.EmissionEnergyMultiplier = 2f * fade;
    }
}
