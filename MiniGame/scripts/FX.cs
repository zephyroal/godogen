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
    private OmniLight3D _light;
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

        // dynamic point light: the blast briefly illuminates nearby walls and characters
        _light = new OmniLight3D
        {
            LightColor = new Color(1f, 0.75f, 0.35f),
            LightEnergy = 6f,
            OmniRange = _radius * 1.2f,
            OmniAttenuation = 1.0f,
            ShadowEnabled = false,
        };
        AddChild(_light);

        var debris = FX.BlockBurst(Vector3.Zero, new Color(1f, 0.75f, 0.35f), 18);
        AddChild(debris);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float k = Mathf.Min(1f, _t / 0.28f);
        _sphere.Scale = Vector3.One * Mathf.Lerp(0.2f, _radius * 0.9f, k);
        if (_light != null)
            _light.LightEnergy = 6f * (1f - k * 0.7f); // fades with the blast
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
            AlbedoColor = new Color(1f, 1f, 1f, 0.95f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.85f, 0.4f),
            EmissionEnergyMultiplier = 6f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = true,
        };
        _beam = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.2f, 1.2f, len), Material = _mat },
        };
        // orient the beam's Z axis (its length) along the direction vector
        if (len > 0.01f)
        {
            var n = dir / len;
            // Godot Basis columns are X, Y, Z. We want Z = n.
            // Pick an arbitrary up that isn't parallel to n.
            var up = Mathf.Abs(n.Y) < 0.99f ? Vector3.Up : Vector3.Forward;
            var x = up.Cross(n).Normalized();
            var y = n.Cross(x).Normalized();
            _beam.Transform = new Transform3D(new Basis(x, y, n), Vector3.Zero);
        }
        AddChild(_beam);

        // dynamic point light at the impact point
        AddChild(new OmniLight3D
        {
            Position = _to - Position, // local offset to the target end
            LightColor = new Color(1f, 0.85f, 0.4f),
            LightEnergy = 4f,
            OmniRange = 5f,
            OmniAttenuation = 1.0f,
            ShadowEnabled = false,
        });
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        // full-bright flash phase: visible at peak for the first 0.15s
        if (_t < 0.15f)
        {
            _beam.Scale = new Vector3(2f, 2f, 1f);
            _mat.EmissionEnergyMultiplier = 8f;
            _mat.AlbedoColor = new Color(1f, 1f, 1f, 0.95f);
        }
        else
        {
            float fade = 1f - (_t - 0.15f) / 0.4f;
            if (fade <= 0f)
            {
                QueueFree();
                return;
            }
            float w = Mathf.Lerp(2f, 0.2f, 1f - fade);
            _beam.Scale = new Vector3(w, w, 1f);
            _mat.AlbedoColor = new Color(1f, 0.85f, 0.4f, 0.95f * fade);
            _mat.EmissionEnergyMultiplier = 8f * fade;
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
            AlbedoColor = new Color(1f, 1f, 1f, 0.9f),
            EmissionEnabled = true,
            Emission = _color.Lightened(0.4f),
            EmissionEnergyMultiplier = 4f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
        };
        _ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.5f, OuterRadius = 0.8f, Material = _ringMat },
            RotationDegrees = new Vector3(90f, 0f, 0f),
            Position = new Vector3(0f, 0.15f, 0f),
        };
        AddChild(_ring);

        // dynamic point light: the impact briefly flashes nearby surfaces
        AddChild(new OmniLight3D
        {
            LightColor = _color.Lightened(0.4f),
            LightEnergy = 3f,
            OmniRange = 4f,
            OmniAttenuation = 1.0f,
            ShadowEnabled = false,
        });
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float k = Mathf.Min(1f, _t / 0.45f);
        _ring.Scale = Vector3.One * Mathf.Lerp(1f, 6f, k);
        float fade = 1f - k;
        if (fade <= 0f)
        {
            QueueFree();
            return;
        }
        _ringMat.AlbedoColor = new Color(1f, 1f, 1f, 0.9f * fade);
        _ringMat.EmissionEnergyMultiplier = 4f * fade;
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
            Mesh = new BoxMesh { Size = new Vector3(1.4f, 2.4f, 0.5f), Material = _mat },
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
