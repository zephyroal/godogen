using System.Collections.Generic;
using Godot;

namespace ParkingGame;

public record BoxDef(Vector3 Center, Vector3 Size);
public record ParkedDef(Vector3 Pos, float YawDeg, Color Color);

/// <summary>Per-level weather: sky/sun/ambient/fog/particles and a grip scale
/// applied to the player car's wheels (snow halves the friction).</summary>
public enum WeatherKind { Sunny, Rain, Snow, Blaze }

/// <summary>A scenario definition — everything the level builder and the slot
/// check need. Slot convention: the LONG axis is local X; SlotYawDeg rotates it.
/// Entry side is the +Z face of the slot in its own frame.</summary>
public class LevelDef
{
    public string Title = "";
    public string Hint = "";
    public Vector3 Spawn;
    public float SpawnYawDeg;
    public Vector3 SlotCenter;
    public float SlotYawDeg;
    public float SlotLen = 6.0f;
    public float SlotWid = 2.5f;
    public float AngleTolDeg = 15f;
    public WeatherKind Weather = WeatherKind.Sunny;
    public bool RoadMarkings = true; // dashed lane/arrow/zebra paint (off in the alley level)
    public float CamH = 15f, CamBack = 7f;
    public List<BoxDef> Walls { get; } = new();
    public List<ParkedDef> Parked { get; } = new();
    public List<Vector3> Cones { get; } = new();
    public List<HazardDef> Hazards { get; } = new();

    public static readonly LevelDef[] All =
    {
        L1(), L2(), L3(), L4(), L5(), L6(), L7(), L8(), L9(),
    };

    private static readonly Color Silver = new(0.75f, 0.77f, 0.80f);
    private static readonly Color Blue = new(0.25f, 0.40f, 0.75f);
    private static readonly Color Green = new(0.30f, 0.60f, 0.40f);
    private static readonly Color Yellow = new(0.90f, 0.75f, 0.20f);

    private static LevelDef L1() => new()
    {
        Title = "第 1 关 · 侧方入库",
        Hint = "开过车位后挂 R 倒回：右打满 → 回正 → 左打满",
        Spawn = new Vector3(13.8f, 0.8f, 2.2f), SpawnYawDeg = -90f,
        SlotCenter = new Vector3(6.5f, 0, -2.6f), SlotLen = 6.0f, SlotWid = 2.5f,
        Walls =
        {
            new BoxDef(new Vector3(7f, 0.175f, -4.6f), new Vector3(24f, 0.35f, 0.35f)),
            new BoxDef(new Vector3(7f, 0.5f, 6.6f), new Vector3(24f, 1f, 0.4f)),
            new BoxDef(new Vector3(-4.5f, 0.5f, 1.0f), new Vector3(0.4f, 1f, 11f)),
            new BoxDef(new Vector3(18.5f, 0.5f, 1.0f), new Vector3(0.4f, 1f, 11f)),
        },
        Parked =
        {
            new ParkedDef(new Vector3(0.9f, 0, -2.6f), 0f, Silver),
            new ParkedDef(new Vector3(12.1f, 0, -2.6f), 0f, Blue),
        },
        Hazards =
        {
            // ambient walker on the far-east boundary — never crosses the demo arc
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(17.0f, 0, 4.5f), new Vector3(17.0f, 0, -4.0f) }, 1.0f),
        },
    };

    private static LevelDef L2() => new()
    {
        Title = "第 2 关 · 窄位极限",
        Hint = "5.3 米车位对 4.6 米车身——前后只有 0.35 米余量，当心路上行人",
        Spawn = new Vector3(13.8f, 0.8f, 2.0f), SpawnYawDeg = -90f,
        SlotCenter = new Vector3(6.5f, 0, -2.6f), SlotLen = 5.3f, SlotWid = 2.4f,
        Walls =
        {
            new BoxDef(new Vector3(7f, 0.175f, -4.35f), new Vector3(24f, 0.35f, 0.35f)),
            new BoxDef(new Vector3(7f, 0.5f, 5.4f), new Vector3(24f, 1f, 0.4f)),
            new BoxDef(new Vector3(-4.5f, 0.5f, 0.5f), new Vector3(0.4f, 1f, 11f)),
            new BoxDef(new Vector3(18.5f, 0.5f, 0.5f), new Vector3(0.4f, 1f, 11f)),
        },
        Parked =
        {
            new ParkedDef(new Vector3(0.65f, 0, -2.6f), 0f, Silver),
            new ParkedDef(new Vector3(12.35f, 0, -2.6f), 0f, Blue),
        },
        Hazards =
        {
            // crosses the road right where the S-approach runs
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(2f, 0, 0.8f), new Vector3(11f, 0, 0.8f) }, 1.1f),
        },
    };

    private static LevelDef L3() => new()
    {
        Title = "第 3 关 · 直角巷道",
        Hint = "先直线倒出巷道口，再摆尾进左侧车位——巷道里也有行人走动",
        Spawn = new Vector3(12f, 0.8f, 15.5f), SpawnYawDeg = 180f,
        RoadMarkings = false,
        SlotCenter = new Vector3(5.5f, 0, -3.0f), SlotLen = 6.0f, SlotWid = 2.5f,
        CamH = 17f, CamBack = 8f,
        Walls =
        {
            new BoxDef(new Vector3(2.6f, 0.5f, 0f), new Vector3(14.8f, 1f, 0.4f)),
            new BoxDef(new Vector3(18.2f, 0.5f, 0f), new Vector3(8.4f, 1f, 0.4f)),
            new BoxDef(new Vector3(9.8f, 0.5f, 9.2f), new Vector3(0.4f, 1f, 18.8f)),
            new BoxDef(new Vector3(14.2f, 0.5f, 9.2f), new Vector3(0.4f, 1f, 18.8f)),
            new BoxDef(new Vector3(12f, 0.5f, 18.6f), new Vector3(4.8f, 1f, 0.4f)),
            new BoxDef(new Vector3(6f, 0.5f, -6.6f), new Vector3(26f, 1f, 0.4f)),
            new BoxDef(new Vector3(-5.2f, 0.5f, -3.3f), new Vector3(0.4f, 1f, 6.6f)),
            new BoxDef(new Vector3(17.2f, 0.5f, -3.3f), new Vector3(0.4f, 1f, 6.6f)),
        },
        Parked =
        {
            new ParkedDef(new Vector3(0.0f, 0, -3.0f), 0f, Green),
            new ParkedDef(new Vector3(11.0f, 0, -3.0f), 0f, Yellow),
        },
        Hazards =
        {
            // walks the lower lot, right across the swing-out zone
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(0f, 0, -1.0f), new Vector3(8f, 0, -1.0f) }, 1.0f),
            // ambles up and down the alley behind the spawn point
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(12f, 0, 6f), new Vector3(12f, 0, 11f) }, 0.9f),
        },
    };

    private static LevelDef L4() => new()
    {
        Title = "第 4 关 · 斜列式车位",
        Hint = "45° 斜位——控制好倒车角度，别蹭邻车，注意巡逻车",
        Spawn = new Vector3(14.8f, 0.8f, 3.0f), SpawnYawDeg = -90f,
        SlotCenter = new Vector3(7f, 0, -2.8f), SlotYawDeg = 45f,
        SlotLen = 5.6f, SlotWid = 2.6f,
        CamH = 16f,
        Walls =
        {
            new BoxDef(new Vector3(7f, 0.5f, -7.6f), new Vector3(26f, 1f, 0.4f)),
            new BoxDef(new Vector3(7f, 0.5f, 6.2f), new Vector3(26f, 1f, 0.4f)),
            new BoxDef(new Vector3(-5.6f, 0.5f, -0.7f), new Vector3(0.4f, 1f, 14f)),
            new BoxDef(new Vector3(19.6f, 0.5f, -0.7f), new Vector3(0.4f, 1f, 14f)),
        },
        Parked =
        {
            new ParkedDef(new Vector3(0.4f, 0, -2.8f), 45f, Silver),
            new ParkedDef(new Vector3(13.6f, 0, -2.8f), 45f, Blue),
        },
        Hazards =
        {
            // crosses the diagonal approach; the amber patrol car laps the road
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(2f, 0, 1.1f), new Vector3(8f, 0, 1.1f) }, 1.1f),
            new HazardDef(HazardKind.PatrolCar,
                new[] { new Vector3(-3f, 0, 3.9f), new Vector3(10f, 0, 3.9f),
                        new Vector3(10f, 0, 2.4f), new Vector3(-3f, 0, 2.4f) }, 2.6f),
        },
    };

    private static LevelDef L5() => new()
    {
        Title = "第 5 关 · 障碍绕行",
        Hint = "避开锥桶与横停车辆——碰撞会记入成绩，行人正在穿行",
        Spawn = new Vector3(14.2f, 0.8f, 2.4f), SpawnYawDeg = -90f,
        SlotCenter = new Vector3(6.5f, 0, -2.6f), SlotLen = 6.0f, SlotWid = 2.5f,
        Walls =
        {
            new BoxDef(new Vector3(7f, 0.175f, -4.6f), new Vector3(24f, 0.35f, 0.35f)),
            new BoxDef(new Vector3(7f, 0.5f, 6.4f), new Vector3(24f, 1f, 0.4f)),
            new BoxDef(new Vector3(-4.5f, 0.5f, 0.9f), new Vector3(0.4f, 1f, 13f)),
            new BoxDef(new Vector3(18.5f, 0.5f, 0.9f), new Vector3(0.4f, 1f, 13f)),
        },
        Parked =
        {
            new ParkedDef(new Vector3(0.9f, 0, -2.6f), 0f, Silver),
            new ParkedDef(new Vector3(12.1f, 0, -2.6f), 0f, Blue),
            new ParkedDef(new Vector3(9.5f, 0, 3.4f), 0f, Yellow), // 横在车道上的车
        },
        Cones =
        {
            new Vector3(3.5f, 0, 1.7f),
            new Vector3(5.5f, 0, 3.6f),
            new Vector3(8.0f, 0, 1.6f),
            new Vector3(10.8f, 0, 5.4f),
        },
        Hazards =
        {
            // one crossing the shuffling zone, one pacing the far-east strip
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(2f, 0, 0.2f), new Vector3(14f, 0, 0.2f) }, 1.1f),
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(16.5f, 0, 5.5f), new Vector3(16.5f, 0, -0.8f) }, 0.9f),
        },
    };

    private static LevelDef L6() => new()
    {
        Title = "第 6 关 · 墙缝极限",
        Hint = "库宽 2.15 米、车宽 1.8 米——先摆正车身，再直线倒进，留意两侧行人",
        Spawn = new Vector3(0.5f, 0.8f, 2.9f), SpawnYawDeg = 180f,
        SlotCenter = new Vector3(0, 0, -3.2f), SlotYawDeg = 90f,
        SlotLen = 5.5f, SlotWid = 2.15f, AngleTolDeg = 10f,
        CamH = 14f, CamBack = 6f,
        Walls =
        {
            new BoxDef(new Vector3(-1.275f, 0.6f, -3.4f), new Vector3(0.25f, 1.2f, 6.0f)),
            new BoxDef(new Vector3(1.275f, 0.6f, -3.4f), new Vector3(0.25f, 1.2f, 6.0f)),
            new BoxDef(new Vector3(0, 0.6f, -6.7f), new Vector3(3.2f, 1.2f, 0.3f)),
            new BoxDef(new Vector3(0, 0.5f, 5.8f), new Vector3(16f, 1f, 0.4f)),
            new BoxDef(new Vector3(-8.2f, 0.5f, 1.0f), new Vector3(0.4f, 1f, 10.4f)),
            new BoxDef(new Vector3(8.2f, 0.5f, 1.0f), new Vector3(0.4f, 1f, 10.4f)),
        },
        Parked =
        {
            new ParkedDef(new Vector3(-3.1f, 0, -3.2f), 90f, Green),
            new ParkedDef(new Vector3(3.1f, 0, -3.2f), 90f, Yellow),
        },
        Hazards =
        {
            // walkers on both flanks of the reverse corridor — straight back stays clear
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(2.5f, 0, 1.2f), new Vector3(6.5f, 0, 1.2f) }, 1.0f),
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(-6.5f, 0, 1.2f), new Vector3(-2.5f, 0, 1.2f) }, 1.2f),
        },
    };

    private static LevelDef L7() => new()
    {
        Title = "第 7 关 · 暴雨侧位",
        Hint = "雨天路滑——刹车距离变长，提前减速再回方向",
        Spawn = new Vector3(13.8f, 0.8f, 2.2f), SpawnYawDeg = -90f,
        SlotCenter = new Vector3(6.5f, 0, -2.6f), SlotLen = 5.8f, SlotWid = 2.5f,
        Weather = WeatherKind.Rain,
        Walls =
        {
            new BoxDef(new Vector3(7f, 0.175f, -4.6f), new Vector3(24f, 0.35f, 0.35f)),
            new BoxDef(new Vector3(7f, 0.5f, 6.6f), new Vector3(24f, 1f, 0.4f)),
            new BoxDef(new Vector3(-4.5f, 0.5f, 1.0f), new Vector3(0.4f, 1f, 11f)),
            new BoxDef(new Vector3(18.5f, 0.5f, 1.0f), new Vector3(0.4f, 1f, 11f)),
        },
        Parked =
        {
            new ParkedDef(new Vector3(0.9f, 0, -2.6f), 0f, Silver),
            new ParkedDef(new Vector3(12.1f, 0, -2.6f), 0f, Blue),
        },
        Hazards =
        {
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(2f, 0, 0.8f), new Vector3(11f, 0, 0.8f) }, 1.1f),
        },
    };

    private static LevelDef L8() => new()
    {
        Title = "第 8 关 · 风雪窄巷",
        Hint = "雪地抓地力只剩一半——动作更慢、方向更柔",
        Spawn = new Vector3(13.8f, 0.8f, 2.0f), SpawnYawDeg = -90f,
        SlotCenter = new Vector3(6.5f, 0, -2.6f), SlotLen = 5.2f, SlotWid = 2.35f,
        AngleTolDeg = 12f,
        Weather = WeatherKind.Snow,
        Walls =
        {
            new BoxDef(new Vector3(7f, 0.175f, -4.35f), new Vector3(24f, 0.35f, 0.35f)),
            new BoxDef(new Vector3(7f, 0.5f, 5.4f), new Vector3(24f, 1f, 0.4f)),
            new BoxDef(new Vector3(-4.5f, 0.5f, 0.5f), new Vector3(0.4f, 1f, 11f)),
            new BoxDef(new Vector3(18.5f, 0.5f, 0.5f), new Vector3(0.4f, 1f, 11f)),
        },
        Parked =
        {
            new ParkedDef(new Vector3(0.65f, 0, -2.6f), 0f, Silver),
            new ParkedDef(new Vector3(12.35f, 0, -2.6f), 0f, Blue),
        },
        Hazards =
        {
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(3f, 0, 0.6f), new Vector3(10f, 0, 0.6f) }, 1.0f),
        },
    };

    private static LevelDef L9() => new()
    {
        Title = "第 9 关 · 烈日广场",
        Hint = "烈日刺眼——广场上有巡场车和行人，别慌",
        Spawn = new Vector3(14.8f, 0.8f, 3.0f), SpawnYawDeg = -90f,
        SlotCenter = new Vector3(7f, 0, -2.8f), SlotYawDeg = 45f,
        SlotLen = 5.6f, SlotWid = 2.6f,
        Weather = WeatherKind.Blaze,
        Walls =
        {
            new BoxDef(new Vector3(7f, 0.5f, -7.6f), new Vector3(26f, 1f, 0.4f)),
            new BoxDef(new Vector3(7f, 0.5f, 6.2f), new Vector3(26f, 1f, 0.4f)),
            new BoxDef(new Vector3(-5.6f, 0.5f, -0.7f), new Vector3(0.4f, 1f, 14f)),
            new BoxDef(new Vector3(19.6f, 0.5f, -0.7f), new Vector3(0.4f, 1f, 14f)),
        },
        Parked =
        {
            new ParkedDef(new Vector3(0.4f, 0, -2.8f), 45f, Silver),
            new ParkedDef(new Vector3(13.6f, 0, -2.8f), 45f, Blue),
        },
        Hazards =
        {
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(2f, 0, 1.1f), new Vector3(8f, 0, 1.1f) }, 1.1f),
            new HazardDef(HazardKind.Pedestrian,
                new[] { new Vector3(11.5f, 0, 5.0f), new Vector3(11.5f, 0, 1.4f) }, 0.9f),
            new HazardDef(HazardKind.PatrolCar,
                new[] { new Vector3(-3f, 0, 3.9f), new Vector3(10f, 0, 3.9f),
                        new Vector3(10f, 0, 2.4f), new Vector3(-3f, 0, 2.4f) }, 2.6f),
        },
    };
}

/// <summary>Builds one scenario procedurally: floor, painted slot, walls,
/// parked cars, cones, moving hazards. Static geometry plus kinematic
/// hazards — the only dynamic physics body is the player car.</summary>
public partial class Level : Node3D
{
    public LevelDef Def = new();

    public static Level Build(LevelDef def)
    {
        var level = new Level { Name = "Level", Def = def };
        level.AddStaticBox(new BoxDef(new Vector3(0, -0.5f, 0), new Vector3(90, 1, 90)),
            new Color(0.24f, 0.25f, 0.27f), "Floor");
        level.PaintSlot(def);
        if (def.RoadMarkings)
            level.PaintRoad(def);
        foreach (var w in def.Walls)
            level.AddStaticBox(w, new Color(0.72f, 0.72f, 0.70f), "Obstacle");
        foreach (var p in def.Parked)
            level.AddParkedCar(p);
        foreach (var c in def.Cones)
            level.AddCone(c);
        foreach (var h in def.Hazards)
            level.AddChild(Hazard.Build(h));
        return level;
    }

    private void AddStaticBox(BoxDef box, Color color, string name)
    {
        var body = new StaticBody3D { Name = name, Position = box.Center };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = box.Size },
        });
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = box.Size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
        };
        body.AddChild(mesh);
        AddChild(body);
    }

    private void PaintSlot(LevelDef def)
    {
        var pivot = new Node3D
        {
            Position = def.SlotCenter,
            Rotation = new Vector3(0, Mathf.DegToRad(def.SlotYawDeg), 0),
        };
        var fill = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(def.SlotLen, 0.02f, def.SlotWid) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.25f, 0.68f, 0.36f, 0.32f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Position = new Vector3(0, 0.015f, 0),
        };
        pivot.AddChild(fill);

        // white border strips (long axis = local X); emissive so the Glow
        // post-process picks them up as freshly painted markings
        var white = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.92f, 0.90f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.72f, 0.72f, 0.66f),
            EmissionEnergyMultiplier = 0.9f,
        };
        float th = 0.10f, h = 0.03f;
        var strips = new[]
        {
            new Vector3(0, 0.02f, -def.SlotWid / 2f), new Vector3(0, 0.02f, def.SlotWid / 2f),
        };
        foreach (var s in strips)
        {
            pivot.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(def.SlotLen + th, h, th) },
                MaterialOverride = white,
                Position = s,
            });
        }
        foreach (var s in new[] { new Vector3(-def.SlotLen / 2f, 0.02f, 0), new Vector3(def.SlotLen / 2f, 0.02f, 0) })
        {
            pivot.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(th, h, def.SlotWid) },
                MaterialOverride = white,
                Position = s,
            });
        }
        AddChild(pivot);
    }

    private void PaintRoad(LevelDef def)
    {
        // generic lot markings derived from the slot position: a dashed yellow
        // centerline, a west-pointing arrow on the approach lane, and a zebra
        // crossing on the far side of the road. Pure paint — no colliders, so
        // hazards' ray probes ignore it. y 0.044 sits above the slot strips and
        // below the skid marks (0.064) to avoid z-fighting.
        float laneZ = def.SlotCenter.Z + def.SlotWid / 2f + 2.2f;
        var yellow = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.72f, 0.08f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.5f, 0.4f, 0.05f),
            EmissionEnergyMultiplier = 0.55f,
        };
        var white = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.92f, 0.90f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.72f, 0.72f, 0.66f),
            EmissionEnergyMultiplier = 0.6f,
        };

        for (float x = def.SlotCenter.X - 7f; x <= def.SlotCenter.X + 7f; x += 3.4f)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.8f, 0.02f, 0.12f) },
                MaterialOverride = yellow,
                Position = new Vector3(x, 0.044f, laneZ),
            });
        }

        // arrow: shaft + chevron head, pointing west along the approach lane
        float ax = def.SlotCenter.X + 6f;
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.3f, 0.02f, 0.18f) },
            MaterialOverride = white,
            Position = new Vector3(ax, 0.044f, laneZ),
        });
        float tipX = ax - 0.85f;
        foreach (var s in new[] { -1, 1 })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.02f, 0.55f) },
                MaterialOverride = white,
                Position = new Vector3(tipX + 0.21f, 0.044f, laneZ + s * 0.2f),
                Rotation = new Vector3(0, -s * 0.7f, 0),
            });
        }

        // zebra crossing
        float zc = def.SlotCenter.Z + def.SlotWid / 2f + 5.3f;
        for (float x = def.SlotCenter.X - 3.5f; x <= def.SlotCenter.X + 3.5f; x += 0.75f)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.02f, 2.2f) },
                MaterialOverride = white,
                Position = new Vector3(x, 0.044f, zc),
            });
        }
    }

    private void AddParkedCar(ParkedDef p)
    {
        var body = new StaticBody3D { Name = "Obstacle", Position = p.Pos,
            Rotation = new Vector3(0, Mathf.DegToRad(p.YawDeg), 0) };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(Car.SedanWid, 1.05f, Car.SedanLen) },
            Position = new Vector3(0, 0.55f, 0),
        });
        var paint = new StandardMaterial3D { AlbedoColor = p.Color, Roughness = 0.4f, Metallic = 0.1f };
        var chassis = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Car.SedanWid, 0.55f, Car.SedanLen) },
            MaterialOverride = paint,
            Position = new Vector3(0, 0.52f, 0),
        };
        // same glass-band + roof silhouette as the player car (lights off — parked)
        var windows = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Car.SedanWid - 0.14f, 0.30f, 2.16f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 0.13f, 0.16f, 0.85f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.08f,
                Metallic = 0.9f,
            },
            Position = new Vector3(0, 0.95f, 0.25f),
        };
        var roof = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Car.SedanWid - 0.2f, 0.20f, 2.0f) },
            MaterialOverride = paint,
            Position = new Vector3(0, 1.20f, 0.22f),
        };
        body.AddChild(chassis);
        body.AddChild(windows);
        body.AddChild(roof);
        AddChild(body);
    }

    private void AddCone(Vector3 pos)
    {
        var body = new StaticBody3D { Name = "Obstacle", Position = pos + new Vector3(0, 0.3f, 0) };
        body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = 0.16f, Height = 0.55f },
        });
        body.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.10f, BottomRadius = 0.18f, Height = 0.55f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.45f, 0.1f) },
        });
        AddChild(body);
    }
}
