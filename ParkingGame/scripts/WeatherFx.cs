using Godot;

namespace ParkingGame;

/// <summary>Per-level weather: sky/ambient/sun/fog/glow tuning, a rain-or-snow
/// particle field that follows the camera, and the wheel grip scale (snow
/// halves friction — a real gameplay change, not just a tint).</summary>
public partial class WeatherFx : Node3D
{
    private Environment _env = null!;
    private DirectionalLight3D _sun = null!;
    private ProceduralSkyMaterial _sky = null!;
    private Car _car = null!;
    private Node3D _particles = null!;
    private float _fxHeight = 14f;

    public void Bind(Environment env, DirectionalLight3D sun, ProceduralSkyMaterial sky, Car car)
    {
        _env = env;
        _sun = sun;
        _sky = sky;
        _car = car;
    }

    public override void _Process(double delta)
    {
        // keep the particle field centered over the action
        if (_particles != null && IsInstanceValid(_particles))
        {
            var cam = GetViewport().GetCamera3D();
            if (cam != null)
                _particles.GlobalPosition = new Vector3(cam.GlobalPosition.X, _fxHeight, cam.GlobalPosition.Z);
        }
    }

    public void Apply(WeatherKind kind)
    {
        if (_particles != null)
        {
            _particles.QueueFree();
            _particles = null!;
        }

        switch (kind)
        {
            case WeatherKind.Sunny:
                _sky.SkyTopColor = new Color(0.32f, 0.48f, 0.72f);
                _sky.SkyHorizonColor = new Color(0.78f, 0.82f, 0.88f);
                _env.AmbientLightEnergy = 0.65f;
                _env.GlowIntensity = 0.65f;
                _env.FogEnabled = false;
                _sun.LightEnergy = 1.35f;
                _sun.LightColor = new Color(1f, 0.95f, 0.86f);
                _car.SetGrip(1f);
                break;

            case WeatherKind.Rain:
                _sky.SkyTopColor = new Color(0.20f, 0.24f, 0.30f);
                _sky.SkyHorizonColor = new Color(0.42f, 0.47f, 0.54f);
                _env.AmbientLightEnergy = 0.5f;
                _env.GlowIntensity = 0.5f;
                _env.FogEnabled = true;
                _env.FogLightColor = new Color(0.45f, 0.5f, 0.58f);
                _env.FogDensity = 0.012f;
                _sun.LightEnergy = 0.6f;
                _sun.LightColor = new Color(0.85f, 0.88f, 0.95f);
                _fxHeight = 14f; // rain streaks fall fast (20 m/s) — a thin high band works
                _particles = MakeParticles(new Color(0.62f, 0.72f, 0.90f, 0.45f),
                    new Vector3(0.012f, 0.55f, 0.012f), 420, 1.4f, 20f,
                    new Vector3(18f, 1f, 14f));
                _car.SetGrip(0.78f);
                break;

            case WeatherKind.Snow:
                _sky.SkyTopColor = new Color(0.55f, 0.60f, 0.68f);
                _sky.SkyHorizonColor = new Color(0.82f, 0.85f, 0.90f);
                _env.AmbientLightEnergy = 0.75f;
                _env.GlowIntensity = 0.55f;
                _env.FogEnabled = true;
                _env.FogLightColor = new Color(0.85f, 0.88f, 0.92f);
                _env.FogDensity = 0.008f;
                _sun.LightEnergy = 0.9f;
                _sun.LightColor = new Color(0.95f, 0.97f, 1f);
                // spawn band sits LOW and TALL (y 2-14): snow falls slowly, so a
                // high thin band would hover above the top-down camera frustum
                _fxHeight = 8f;
                _particles = MakeParticles(new Color(1f, 1f, 1f, 0.9f),
                    new Vector3(0.11f, 0.11f, 0.11f), 700, 5f, 1.5f,
                    new Vector3(9f, 6f, 7f));
                _car.SetGrip(0.55f);
                break;

            case WeatherKind.Blaze:
                _sky.SkyTopColor = new Color(0.22f, 0.42f, 0.78f);
                _sky.SkyHorizonColor = new Color(1.0f, 0.78f, 0.5f);
                _env.AmbientLightEnergy = 0.45f;
                _env.GlowIntensity = 0.85f;
                _env.FogEnabled = false;
                _sun.LightEnergy = 2.6f;
                _sun.LightColor = new Color(1f, 0.88f, 0.68f);
                _car.SetGrip(1f);
                break;
        }
    }

    private CpuParticles3D MakeParticles(Color color, Vector3 meshSize, int amount,
        float lifetime, float velocity, Vector3 boxExtents)
    {
        var p = new CpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            Emitting = true,
            Mesh = new BoxMesh { Size = meshSize },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
            Direction = new Vector3(0.15f, -1f, 0.05f),
            Spread = 12f,
            InitialVelocityMin = velocity * 0.85f,
            InitialVelocityMax = velocity * 1.15f,
            Gravity = Vector3.Zero,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = boxExtents,
        };
        AddChild(p);
        return p;
    }
}
