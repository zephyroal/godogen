using Godot;

namespace Xiangqi3D;

/// <summary>Small FX helpers (capture burst). Caller adds the returned node to the tree and frees it.</summary>
public static class FX
{
    private static BoxMesh _debris;

    /// <summary>One-shot cube debris burst (add to the tree, auto-free after ~1.2s via the caller's timer).</summary>
    public static CpuParticles3D Burst(Vector3 pos, Color color, int amount = 16)
    {
        _debris ??= new BoxMesh { Size = new Vector3(0.14f, 0.14f, 0.14f) };
        return new CpuParticles3D
        {
            Position = pos,
            Amount = amount,
            Lifetime = 0.7f,
            OneShot = true,
            Explosiveness = 1f,
            Emitting = true,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 90f,
            Gravity = new Vector3(0f, -12f, 0f),
            InitialVelocityMin = 2.5f,
            InitialVelocityMax = 5.5f,
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.1f,
            Mesh = _debris,
            Color = color,
        };
    }
}
