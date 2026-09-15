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
                _particles.GlobalPosition = new Vector3(cam.GlobalPosition.X, 14f, cam.GlobalPosition.Z);
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
                _particles = MakeParticles(new Color(0.62f, 0.72f, 0.90f, 0.45f),
                    new Vector3(0.012f, 0.55f, 0.012f), 420, 1.4f, 20f);
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
                _particles = MakeParticles(new Color(1f, 1f, 1f, 0.85f),
                    new Vector3(0.06f, 0.06f, 0.06f), 320, 7f, 1.8f);
                _car.SetGrip(0.55f);
                break;

            case WeatherKind.Blaze:
                _sky.SkyTopColor = new Color(0.25f, 0.45f, 0.80f);
                _sky.SkyHorizonColor = new Color(0.95f, 0.85f, 0.68f);
                _env.AmbientLightEnergy = 0.55f;
                _env.GlowIntensity = 0.85f;
                _env.FogEnabled = false;
                _sun.LightEnergy = 1.9f;
                _sun.LightColor = new Color(1f, 0.90f, 0.72f);
                _car.SetGrip(1f);
                break;
        }
    }

    private CPUParticles3D MakeParticles(Color color, Vector3 meshSize, int amount,
        float lifetime, float velocity)
    {
        var p = new CPUParticles3D
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
            EmissionShape = CPUParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(18f, 1f, 14f),
        };
        AddChild(p);
        return p;
    }
}
