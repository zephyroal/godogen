using System.Collections.Generic;
using Godot;

namespace FortressRush;

/// <summary>
/// One fortress: a maze of destructible wall blocks on the three lanes of one side.
/// Complexity scales with Index (1 = simple outpost … 10 = ultimate main city).
/// Total HP = sum of block HP; at zero the fortress collapses into non-blocking ruins.
/// </summary>
public partial class Fortress : Node3D
{
    private static readonly Dictionary<int, StandardMaterial3D> MatCache = new();

    public Team Team;
    public int Index;
    public float ZMin, ZMax;
    public float TotalHp, CurrentHp;
    public bool Destroyed;
    public readonly List<Block> Blocks = new();

    public float CenterZ => (ZMin + ZMax) * 0.5f;
    public Block Core { get; private set; }
    private Label3D _sign;
    private int _hpTicks = -1;

    public Fortress(Team team, int index, float zA, float zB, System.Random rng)
    {
        Team = team;
        Index = index;
        ZMin = Mathf.Min(zA, zB);
        ZMax = Mathf.Max(zA, zB);
        Name = $"Fort_{(team == Team.Blue ? "Blue" : "Red")}{index}";
        Build(rng);
    }

    public bool OwnsLane(int laneIdx) => laneIdx >= Game.LaneBase(Team) && laneIdx < Game.LaneBase(Team) + 3;

    private void Build(System.Random rng)
    {
        int rows = Mathf.Max(2, Mathf.CeilToInt((ZMax - ZMin) / Game.BlockSize) - 1);
        int baseLane = Game.LaneBase(Team);

        float zFront = Team == Team.Blue ? ZMin : ZMax; // edge facing the enemy (toward map center)

        for (int r = 0; r < rows; r++)
        {
            float z = ZMin + 1.5f + r * Game.BlockSize;
            bool gateRow = r == 0;
            int openLane = rng.Next(3);
            if (r == rows - 1 && openLane == 1) openLane = rng.Next(2) * 2; // keep a side lane open beside the core
            for (int l = 0; l < 3; l++)
            {
                int laneIdx = baseLane + l;
                bool isGate = gateRow && l == 1;
                float prob = 0.16f + 0.022f * Index;
                if (r == rows - 1) prob += 0.10f; // protect the core row
                if (isGate || l == openLane) continue;
                if ((float)rng.NextDouble() >= prob) continue;
                int height = Index >= 10 ? 3 : (Index >= 8 || (Index >= 4 && rng.NextDouble() < 0.5)) ? 2 : 1;
                for (int h = 0; h < height; h++)
                    AddWallBlock(laneIdx, z, h);
            }
        }

        // core crystal on the last row, middle lane
        float coreZ = ZMin + 1.5f + (rows - 1) * Game.BlockSize;
        Core = MakeBlock(Game.LaneX[baseLane + 1], 2.2f, coreZ, CoreMat(), hp: 90f + 12f * Index, laneIdx: baseLane + 1, isCore: true);
        Core.Scale = new Vector3(0.87f, 0.73f, 0.87f);
        Core.RotationDegrees = new Vector3(0f, 45f, 0f);

        // glowing gate portal at the fortress entrance (middle lane, enemy-facing edge)
        BuildGate(zFront, baseLane);

        // corner pillars (outside the lanes — target practice + HP pool, capped with battlements)
        float[] pillarX =
        {
            Game.LaneX[baseLane] - 3.5f,
            (Game.LaneX[baseLane + 1] + Game.LaneX[baseLane + 2]) * 0.5f,
        };
        float[] pillarZ = { zFront, (ZMin + ZMax) - zFront };
        foreach (var px in pillarX)
            foreach (var pz in pillarZ)
            {
                for (int h = 0; h < 2; h++)
                {
                    var p = MakeBlock(px, 1.5f + h * Game.BlockSize, pz, WallMat(rng), hp: 40f, laneIdx: -1, isCore: false);
                    p.Scale = new Vector3(0.5f, 1f, 0.5f);
                }
                AddBattlement(px, pz);
            }

        // the ultimate main city wears a golden crown on its crystal
        if (Index == Game.FortressCount)
        {
            var crown = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(1.3f, 0.7f, 1.3f),
                    Material = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(1f, 0.82f, 0.25f),
                        EmissionEnabled = true,
                        Emission = new Color(1f, 0.8f, 0.2f),
                        EmissionEnergyMultiplier = 1.2f,
                    },
                },
                Position = new Vector3(Game.LaneX[baseLane + 1], 5.2f, coreZ),
            };
            AddChild(crown);
        }

        TotalHp = CurrentHp = SumBlockHp();

        // floating sign: 编号 + HP bar
        _sign = new Label3D
        {
            Text = SignText(),
            Position = new Vector3(Game.LaneX[baseLane + 1], 10.5f, zFront + (Team == Team.Blue ? -1.5f : 1.5f)),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 64,
            OutlineSize = 12,
            Modulate = Game.ColorOf(Team),
            Font = SharedFont(),
        };
        AddChild(_sign);
    }

    private float SumBlockHp()
    {
        float s = 0f;
        foreach (var b in Blocks) s += b.Hp;
        return s;
    }

    private void AddWallBlock(int laneIdx, float z, int level)
    {
        var rng = Game.Instance?.Rng;
        float y = 1.5f + level * Game.BlockSize;
        MakeBlock(Game.LaneX[laneIdx], y, z, WallMat(rng), hp: 50f, laneIdx: laneIdx, isCore: false);
    }

    private Block MakeBlock(float x, float y, float z, StandardMaterial3D mat, float hp, int laneIdx, bool isCore)
    {
        var b = new Block
        {
            Mesh = SharedBox(),
            Hp = hp,
            Fortress = this,
            IsCore = isCore,
            LaneIdx = laneIdx,
            Position = new Vector3(x, y, z),
            MaterialOverride = mat,
        };
        Blocks.Add(b);
        AddChild(b);
        return b;
    }

    private static BoxMesh SharedBox()
    {
        if (_box == null) _box = new BoxMesh { Size = new Vector3(Game.BlockSize, Game.BlockSize, Game.BlockSize) };
        return _box;
    }
    private static BoxMesh _box;

    private static SystemFont SharedFont()
    {
        if (_font == null)
            _font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "SimHei", "Segoe UI", "sans-serif" } };
        return _font;
    }
    private static SystemFont _font;

    private StandardMaterial3D WallMat(System.Random rng)
    {
        int shade = rng?.Next(4) ?? 0;
        int key = ((int)Team + 1) * 100 + shade;
        if (!MatCache.TryGetValue(key, out var mat))
        {
            var c = Game.ColorOf(Team);
            float f = 1f - shade * 0.13f;
            mat = new StandardMaterial3D { AlbedoColor = new Color(c.R * f, c.G * f, c.B * f), Roughness = 0.9f };
            MatCache[key] = mat;
        }
        return mat;
    }

    private StandardMaterial3D CoreMat()
    {
        int key = ((int)Team + 1) * 100 + 9;
        if (!MatCache.TryGetValue(key, out var mat))
        {
            var c = Game.ColorOf(Team);
            mat = new StandardMaterial3D
            {
                AlbedoColor = c.Lightened(0.15f),
                EmissionEnabled = true,
                Emission = c,
                EmissionEnergyMultiplier = 1.6f,
                Roughness = 0.4f,
            };
            MatCache[key] = mat;
        }
        return mat;
    }

    /// <summary>Glowing portal frame at the fortress gate (decorative, never blocks the lane).</summary>
    private void BuildGate(float zFront, int baseLane)
    {
        var c = Game.ColorOf(Team);
        float gateX = Game.LaneX[baseLane + 1];
        float gateZ = zFront + (Team == Team.Blue ? -1.8f : 1.8f);
        float height = Index == Game.FortressCount ? 5.6f : 4.4f;

        var frameMat = new StandardMaterial3D { AlbedoColor = c.Darkened(0.25f), Roughness = 0.9f };
        var post = new BoxMesh { Size = new Vector3(0.8f, height, 0.8f), Material = frameMat };
        foreach (float off in new[] { -2.4f, 2.4f })
            AddChild(new MeshInstance3D { Mesh = post, Position = new Vector3(gateX + off, height * 0.5f, gateZ) });
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(5.6f, 0.8f, 0.8f), Material = frameMat },
            Position = new Vector3(gateX, height, gateZ),
        });

        var glowMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(c.R, c.G, c.B, 0.32f),
            EmissionEnabled = true,
            Emission = c,
            EmissionEnergyMultiplier = 1.4f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(4f, height - 0.6f, 0.22f), Material = glowMat },
            Position = new Vector3(gateX, (height - 0.6f) * 0.5f, gateZ),
        });
    }

    /// <summary>Battlement cap on top of a pillar (decorative).</summary>
    private void AddBattlement(float px, float pz)
    {
        var c = Game.ColorOf(Team);
        var capMat = new StandardMaterial3D { AlbedoColor = c.Lightened(0.08f), Roughness = 0.9f };
        var cap = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(2f, 0.35f, 2f), Material = capMat },
            Position = new Vector3(px, 6.15f, pz),
        };
        AddChild(cap);
        var merlon = new BoxMesh { Size = new Vector3(0.6f, 0.55f, 0.6f), Material = capMat };
        foreach (float ox in new[] { -0.65f, 0.65f })
            AddChild(new MeshInstance3D { Mesh = merlon, Position = new Vector3(px + ox, 6.6f, pz) });
    }

    // ---- damage / collapse ----

    public void DamageBlock(Block b, float dmg)
    {
        if (Destroyed || !Blocks.Contains(b)) return;
        CurrentHp -= dmg;
        b.TakeDamage(dmg); // may remove itself
        RefreshSign();
        if (CurrentHp <= 0f && !Destroyed) Collapse();
    }

    public void OnBlockRemoved(Block b) => Blocks.Remove(b);

    public void PulseCore(float scale)
    {
        if (!Destroyed && Core != null && IsInstanceValid(Core))
            Core.Scale = new Vector3(0.87f, 0.73f, 0.87f) * scale;
    }

    private void Collapse()
    {
        Destroyed = true;
        foreach (var b in Blocks)
        {
            if (IsInstanceValid(b))
            {
                var fx = FX.BlockBurst(b.GlobalPosition, Game.ColorOf(Team), 10);
                Game.Instance?.AddChild(fx);
                b.QueueFree();
            }
        }
        Blocks.Clear();

        // rubble: flat gray boxes, purely visual, never block lanes
        var rubbleMat = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.45f, 0.47f), Roughness = 1f };
        int baseLane = Game.LaneBase(Team);
        for (int i = 0; i < 9; i++)
        {
            var r = new MeshInstance3D
            {
                Mesh = SharedBox(),
                MaterialOverride = rubbleMat,
                Position = new Vector3(
                    Game.LaneX[baseLane + i % 3] + (i - 4) * 0.4f,
                    0.2f,
                    Mathf.Lerp(ZMin, ZMax, (i + 0.5f) / 9f)),
                RotationDegrees = new Vector3(0f, i * 37f, 0f),
                Scale = new Vector3(0.5f, 0.12f, 0.5f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(r);
        }

        _sign.Modulate = new Color(0.55f, 0.55f, 0.55f);
        RefreshSign();
        Game.Instance?.OnFortressDestroyed(this);
    }

    public Vector3 RespawnPoint()
    {
        int heading = Game.Heading(Team);
        float zFront = heading < 0 ? ZMin - 2.5f : ZMax + 2.5f;
        return new Vector3(Game.LaneX[Game.LaneBase(Team) + 1], 0f, zFront);
    }

    private void RefreshSign()
    {
        if (_sign == null) return;
        int ticks = Mathf.Clamp(Mathf.CeilToInt(CurrentHp / TotalHp * 10f), 0, 10);
        if (ticks == _hpTicks && !Destroyed) return;
        _hpTicks = ticks;
        _sign.Text = SignText();
        float pct = CurrentHp / TotalHp;
        _sign.Modulate = Destroyed ? new Color(0.55f, 0.55f, 0.55f)
            : pct > 0.5f ? Game.ColorOf(Team)
            : pct > 0.25f ? new Color(1f, 0.75f, 0.25f)
            : new Color(1f, 0.35f, 0.3f);
    }

    private string SignText()
    {
        string label = $"{(Team == Team.Blue ? "蓝" : "红")}{Index}" + (Index == Game.FortressCount ? " 主城" : "");
        if (Destroyed) return $"{label} ✗ 已摧毁";
        int ticks = Mathf.Clamp(Mathf.CeilToInt(CurrentHp / TotalHp * 10f), 0, 10);
        string bar = "";
        for (int i = 0; i < 10; i++) bar += i < ticks ? "▮" : "▯";
        return $"{label} {bar} {Mathf.CeilToInt(CurrentHp)}";
    }
}
