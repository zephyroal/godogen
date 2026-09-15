using Godot;

namespace ParkingGame;

/// <summary>One-shot explosion on collision: particle burst (orange → fades)
/// plus a brief OmniLight flash. Camera shake is owned by Game, which owns
/// the camera; this node just spawns the visuals.</summary>
public partial class Effects : Node3D
{
    /// <summary>Total bursts spawned this run — asserted by --verify-features.</summary>
    public int BurstCount { get; private set; }

    public void Explode(Vector3 at)
    {
        BurstCount++;

        var p = new CPUParticles3D
        {
            Position = at + Vector3.Up * 0.7f,
            Emitting = true,
            OneShot = true,
            Amount = 70,
            Lifetime = 0.9f,
            Explosiveness = 1f,
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = 4f,
            InitialVelocityMax = 10f,
            Gravity = new Vector3(0, -9.5f, 0),
            ScaleAmountMin = 0.4f,
            ScaleAmountMax = 1.6f,
            Mesh = new SphereMesh { Radius = 0.16f, Height = 0.32f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.55f, 0.15f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.5f, 0.1f),
                EmissionEnergyMultiplier = 2.5f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        AddChild(p);

        var flash = new OmniLight3D
        {
            Position = at + Vector3.Up * 1.1f,
            LightColor = new Color(1f, 0.6f, 0.2f),
            LightEnergy = 5f,
            OmniRange = 8f,
        };
        AddChild(flash);

        var tw = CreateTween();
        tw.TweenProperty(flash, "light_energy", 0f, 0.35);
        tw.TweenInterval(1.2f);
        tw.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(flash)) flash.QueueFree();
            if (IsInstanceValid(p)) p.QueueFree();
        }));
    }
}
