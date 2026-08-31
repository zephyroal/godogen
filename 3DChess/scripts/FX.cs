using Godot;

namespace Xiangqi3D;

/// <summary>Small visual helpers: capture debris bursts and procedural wood grain.</summary>
public static class FX
{
    private static BoxMesh _debris;

    /// <summary>One-shot cube debris burst (add to the tree, auto-free after ~1.2s via the caller's timer).</summary>
    public static CpuParticles3D Burst(Vector3 pos, Color color, int amount = 16, float vmin = 2.5f, float vmax = 5.5f)
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
            InitialVelocityMin = vmin,
            InitialVelocityMax = vmax,
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.1f,
            Mesh = _debris,
            Color = color,
        };
    }

    /// <summary>Procedural straight-grain wood texture: warped sine bands with per-row meander,
    /// horizontally tileable. Multiplies against the material albedo color.</summary>
    public static ImageTexture WoodGrain(Color light, Color dark, int seed, float bands = 22f, int size = 256)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var rng = new System.Random(seed);
        float p1 = rng.NextSingle() * 9f, p2 = rng.NextSingle() * 9f;
        var warp = new float[size];
        for (int y = 0; y < size; y++)
            warp[y] = 2.4f * Mathf.Sin(y * 0.055f + p1) + 1.4f * Mathf.Sin(y * 0.021f + p2);
        for (int y = 0; y < size; y++)
        {
            float rowTint = 0.94f + rng.NextSingle() * 0.12f;
            for (int x = 0; x < size; x++)
            {
                float g = Mathf.Sin(x / (float)size * Mathf.Tau * bands + warp[y]);
                float v = Mathf.Pow(0.5f + 0.5f * g, 0.6f) * rowTint + (rng.NextSingle() - 0.5f) * 0.06f;
                img.SetPixel(x, y, dark.Lerp(light, Mathf.Clamp(v, 0f, 1f)));
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
